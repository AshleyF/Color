open System
open System.IO
open System.Globalization
open Devices
open Serdes

// console (TODO: ANSI commands on Linux)

let width, height = 80, 40

let empty () = Array2D.create width height (White, Black, ' ')
let current = ref (empty ())
let last = ref (empty ())
let x, y = ref 0, ref 0

let consoleBeep () = Console.Beep()
let consoleRead () = Console.ReadKey(true).KeyChar

let consoleClear () = // Console.Clear()
    current := empty ()
    x := 0; y := 0

let consoleWriteLine () = // Console.WriteLine()
    x := 0
    y := min (height - 1) (!y + 1)

let consoleWrite f b (s : string) =
    let write c =
        (!current).[!x, !y] <- (f, b, c)
        x := !x + 1
        if !x = width then consoleWriteLine ()
    Seq.iter write s

let consoleWriteStatus (s : string) =
    x := 0; y := height - 3 // Console.SetCursorPosition(0, height - 3)
    consoleWrite White Black (s.Substring(0, min (s.Length) (width - 1)))

let consoleRefresh () =
    let color = function
        | Red    -> ConsoleColor.Red
        | Yellow -> ConsoleColor.Yellow
        | Green  -> ConsoleColor.Green
        | White  -> ConsoleColor.White
        | Gray   -> ConsoleColor.Gray
        | Black  -> ConsoleColor.Black
    let move = ref true
    let write x y ((f, b, (c : char)) as z) =
        if z <> (!last).[x, y] then
            if !move then
                Console.SetCursorPosition(x, y)
                move := false
            Console.ForegroundColor <- color f
            Console.BackgroundColor <- color b
            Console.Write(c)
        else move := true
    for y in 0 .. height - 1 do
        for x in 0 .. width - 1 do
            write x y (!current).[x, y]
    last := !current

let consoleInit () =
    Console.CursorVisible <- false
    Console.SetWindowSize(width, height)
    Console.SetBufferSize(width, height)

// render

type Mode = Normal | Insert

type State = {
    Block     : int
    Mode      : Mode
    Before    : Token list
    Current   : Token option
    After     : Token list
    Clipboard : Token list
    Undo      : State option
    Redo      : State option
    Message   : string }

let render state =
    let first = ref true
    let render' highlight cursor (c, s) =
        if !first then first := false elif c = Red then consoleWriteLine ()
        if highlight then consoleWrite Black c s
        else consoleWrite c Black s
        if cursor then consoleWrite Black c " "
        consoleWrite c Black " "
    consoleClear ()
    state.Before |> List.rev |> List.iter (render' false false)
    match state.Current with
    | Some c -> render' (state.Mode = Normal) (state.Mode = Insert) c
    | None -> ()
    List.iter (render' false false) state.After
    consoleWriteStatus state.Message
    consoleRefresh ()
    state

// editor

let rec edit state key =
    let checkpoint s =
        let s' = { s with Mode = Normal; Message = "" }
        match s.Undo with
        | Some u ->
            if s.Mode    = u.Mode &&
               s.Before  = u.Before &&
               s.Current = u.Current &&
               s.After   = u.After then s else { s with Undo = Some s' }
        | None -> { s with Undo = Some s' }
    let message s m = { s with Message = m }
    let load b s =
        match loadTokens b with
        | c :: a -> { checkpoint s with Before = []; Current = Some c; After = a; Block = b } |> message <| sprintf "Loaded block %i " b
        | [] -> { checkpoint s with Before = []; Current = None; After = []; Block = b } |> message <| sprintf "Loaded empty block %i" b
    let save s =
        let tokens = (state.Before |> List.rev) @ (match s.Current with Some c -> [c] | None -> []) @ s.After
        saveTokens s.Block tokens
        saveTokens 0 tokens // for assembler
        sprintf "Saved block %i" state.Block |> message s
    let move b c a =
        match b, c with
        | b :: bs, Some c -> bs, Some b, c :: a
        | b :: bs, None -> bs, Some b, a
        | [], _ -> b, c, a
    let edit' b c a s = { s with Before = b; Current = c; After = a }
    let rec left s f =
        let b, c, a = move s.Before s.Current s.After
        let s' = edit' b c a s
        if f c && b <> [] then left s' f else s'
    let rec right s f =
        let a, c, b = move s.After s.Current s.Before
        let s' = edit' b c a s
        if f c && a <> [] then right s' f else s'
    let shift = function
        | x :: xs, y -> xs, Some x, y
        | x, y :: ys -> x, Some y, ys
        | x, y -> x, None, y
    let undo s = match s with { Undo = Some u } -> { u with Redo = Some s } | _ -> s
    let redo s = match s with { Redo = Some r } -> { r with Undo = Some s } | _ -> s
    let delete s =
        let a, c', b = shift (s.After, s.Before)
        match s.Current with
        | Some c -> { edit' b c' a (checkpoint s) with Clipboard = c :: s.Clipboard }
        | None -> failwith "Delete with no current."
    let deleteBack s =
        let b, c', a = shift (s.Before, s.After)
        match s.Current with
        | Some c -> { edit' b c' a (checkpoint s) with Clipboard = c :: s.Clipboard }
        | None -> failwith "Delete with no current."
    let putBefore s =
        match s.Current, s.Clipboard with
        | Some c, p :: ps -> { checkpoint s with Before = c :: s.Before; Current = Some p; Clipboard = ps }
        | _ -> s
    let putAfter s =
        match s.Current, s.Clipboard with
        | Some c, p :: ps -> { checkpoint s with After = c :: s.After; Current = Some p; Clipboard = ps }
        | _ -> s
    let nextColor = function Some (Red, _) -> Some (Green, "") | Some (t, _) -> Some (t, "") | None -> Some (Red, "")
    let insert s =
        let a = match s.Current with Some c -> c :: s.After | None -> s.After
        { checkpoint s with After = a; Current = nextColor s.Current; Mode = Insert }
    let append s =
        let b = match s.Current with Some c -> c :: s.Before | None -> s.Before
        { checkpoint s with Before = b; Current = nextColor s.Current; Mode = Insert }
    let normal s =
        match s.Current with
        | None | Some (_, "") -> match s.Undo with Some u -> u | None -> s
        | _ -> { s with Mode = Normal }
    let input s k =
        match s.Current with
        | Some (t, w) -> { s with Current = Some (t, sprintf "%s%c" w k) }
        | None -> failwith "Key input with no current."
    let del s =
        match s.Current with
        | Some (t, w) -> { s with Current = Some (t, w.Substring(0, w.Length - 1)) }
        | None -> failwith "Del with no current."
    let next s =
        match s.Current with
        | Some (t, w) when w.Length > 0 ->
            { checkpoint { s with Mode = Normal }
                with Mode = Insert; Before = s.Current.Value :: s.Before; Current = nextColor s.Current }
        | _ -> s
    let tag t s =
        match s.Current with
        | Some (_, w) -> { checkpoint s with Current = Some (t, w) }
        | None -> s
    let once = (fun _ -> false)
    let toDef = (function Some (Red, _) -> false | _ -> true)
    try
        match state.Mode, key with
        | _, k when int k = 18 (* ctrl-R *) -> tag Red state
        | _, k when int k = 25 (* ctrl-Y *) -> tag Yellow state
        | _, k when int k = 7  (* ctrl-G *) -> tag Green state
        | _, k when int k = 23 (* ctrl-W *) -> tag White state
        | _, k when int k = 2  (* ctrl-B *) -> tag Gray state
        | Normal, '1' -> load 1 state
        | Normal, '2' -> load 2 state
        | Normal, '3' -> load 3 state
        | Normal, '4' -> load 4 state
        | Normal, '5' -> load 5 state
        | Normal, '6' -> load 6 state
        | Normal, '7' -> load 7 state
        | Normal, '8' -> load 8 state
        | Normal, '9' -> load 9 state
        | Normal, 's' -> save state
        | Normal, 'h' | Normal, 'b' -> left state once
        | Normal, 'l' | Normal, 'w' -> right state once
        | Normal, 'k' -> left state toDef
        | Normal, 'j' -> right state toDef
        | Normal, 'x' -> delete state
        | Normal, 'X' -> deleteBack state
        | Normal, 'P' -> putAfter state
        | Normal, 'p' -> putBefore state
        | Normal, 'u' -> undo state
        | Normal, 'U' -> redo state
        | Normal, 'i' -> insert state
        | Normal, 'a' -> append state
        | Normal, 'c' -> right (tag White state) once
        | Insert, k when k = char 27 (* esc *) -> normal state
        | Insert, ' ' -> next state
        | Insert, '\b' -> del state
        | Insert, k -> input state k
        | _, k -> failwith (sprintf "Invald key (%i)." (int k))
    with ex -> consoleBeep (); message state ex.Message

let main state =
    let noMessage s = { s with Message = "" }
    consoleInit ()
    let rec main' state = consoleRead () |> edit state |> render |> noMessage |> main'
    state |> render |> main'

main { Block = 1; Mode = Normal; Before = []; Current = None; After = []; Clipboard = []; Undo = None; Redo = None; Message = "Welcome to colorForth" }