open System
open System.IO
open Devices
open Serdes
open Console

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

let currentApply f = function { Current = Some c } -> f c | s -> s

let showFormatting = ref false
let showComments = ref true

let render state =
    let render' highlight cursor (c, s) =
        if !showComments || c <> White then
            let c' = if !showFormatting || c <> Blue then c else Black
            if highlight then consoleWrite Black c s else consoleWrite c' Black s
            if cursor then consoleWrite Black c' " "
            consoleWrite c' Black " "
            if c = Blue then
                match s with
                | "cr" -> consoleWriteLine ()
                | "." -> consoleWrite Black Black "  "
                | _ -> ()
    let renderTokens = List.iter (render' false false)
    consoleClear ()
    state.Before |> List.rev |> renderTokens
    currentApply (fun c -> render' (state.Mode = Normal) (state.Mode = Insert) c; state) state |> ignore
    renderTokens state.After
    consoleWriteStatus state.Message
    consoleRefresh ()
    state

let rec edit state key =
    let checkpoint s =
        currentApply (fun (_, w) ->
            if w.Length > 0 then
                let same u = s.Mode = u.Mode && s.Before = u.Before && s.Current = u.Current && s.After = u.After
                let s' = { s with Mode = Normal; Message = "" }
                match s.Undo with
                | Some u -> if same u then s else { s with Undo = Some s' }
                | None -> { s with Undo = Some s' }
            else s) s
    let message s m = { s with Message = m }
    let load b s =
        match loadTokens b with
        | c :: a -> { checkpoint s with Before = []; Current = Some c; After = a; Block = b } |> message <| sprintf "Loaded block %i " b
        | [] -> { checkpoint s with Before = []; Current = None; After = []; Block = b } |> message <| sprintf "Loaded empty block %i" b
    let save s =
        let tokens = (state.Before |> List.rev) @ (match s.Current with Some c -> [c] | None -> []) @ s.After
        saveTokens s.Block tokens
        File.WriteAllText(blockFile s.Block "txt", tokens2text tokens)
        sprintf "Saved block %i" s.Block |> message s
    let move b c a =
        match b, c with
        | b :: bs, Some c -> bs, Some b, c :: a
        | b :: bs, None -> bs, Some b, a
        | [], _ -> b, c, a
    let edit' b c a s = { s with Before = b; Current = c; After = a }
    let rec left f s =
        let b, c, a = move s.Before s.Current s.After
        let s' = edit' b c a s
        if f c && b <> [] then left f s' else s'
    let rec right f s =
        let a, c, b = move s.After s.Current s.Before
        let s' = edit' b c a s
        if f c && a <> [] then right f s' else s'
    let shift = function
        | x :: xs, y -> xs, Some x, y
        | x, y :: ys -> x, Some y, ys
        | x, y -> x, None, y
    let undo s = match s with { Undo = Some u } -> { u with Redo = Some s } | _ -> s
    let redo s = match s with { Redo = Some r } -> { r with Undo = Some s } | _ -> s
    let delete s =
        let a, c', b = shift (s.After, s.Before)
        currentApply (fun c -> { edit' b c' a (checkpoint s) with Clipboard = c :: s.Clipboard }) s
    let deleteBack s =
        let b, c', a = shift (s.Before, s.After)
        currentApply (fun c -> { edit' b c' a (checkpoint s) with Clipboard = c :: s.Clipboard }) s
    let yank s =
        currentApply (fun c ->
            match s.After with
            | a :: a' -> { s with Clipboard = c :: s.Clipboard; Before = c :: s.Before; Current = Some a; After = a' }
            | [] -> { s with Clipboard = c :: s.Clipboard }) s
    let put f s =
        match s.Current, s.Clipboard with
        | Some c, p :: ps -> { checkpoint (f c s) with Current = Some p; Clipboard = ps }
        | _ -> s
    let putBefore s = put (fun c s -> { s with Before = c :: s.Before }) s
    let putAfter  s = put (fun c s -> { s with After  = c :: s.After  }) s
    let nextColor = function Some (Red, _) | Some (Blue, _) -> Some (Green, "") | Some (t, _) -> Some (t, "") | None -> Some (Red, "")
    let insert s =
        let a = match s.Current with Some c -> c :: s.After | None -> s.After
        { checkpoint s with After = a; Current = nextColor s.Current; Mode = Insert }
    let append s =
        let b = match s.Current with Some c -> c :: s.Before | None -> s.Before
        { checkpoint s with Before = b; Current = nextColor s.Current; Mode = Insert }
    let fix s =
        let good (c, (w : string)) = w.Length > 0
        let fix' = List.filter good
        let goodOpt = function Some g -> good g | None -> true
        { s with Before = fix' s.Before; After = fix' s.After; Current = if goodOpt s.Current then s.Current else None }
    let normal s =
        match s.Current with
        | None | Some (_, "") -> match s.Undo with Some u -> u | None -> s
        | _ -> { s with Mode = Normal }
    let input k s =
        let complete = function
            | Gray, "an"   -> "and"
            | Gray, "b"    -> "b!"
            | Gray, "b!e"  -> "begin"
            | Gray, "b!r"  -> "break"
            | Gray, "c"    -> "call"
            | Gray, "d"    -> "dup"
            | Gray, "dupr" -> "drop"
            | Gray, "e"    -> "ex"
            | Gray, "exn"  -> "end"
            | Gray, "j"    -> "jump"
            | Gray, "i"    -> "if"
            | Gray, "m"    -> "mark"
            | Gray, "n"    -> "next"
            | Gray, "o"    -> "or"
            | Gray, "orv"  -> "over"
            | Gray, "p"    -> "pop"
            | Gray, "popu" -> "push"
            | Gray, "popr" -> "print"
            | Gray, "t"    -> "then"
            | Gray, "u"    -> "unext"
            | Gray, "-i"   -> "-if"
            | Gray, "2"    -> "2*"
            | Gray, "2*/"  -> "2/"
            | _, w -> w
        currentApply (fun (t, w) -> { s with Current = Some (t, complete (t, sprintf "%s%c" w k)) }) s
    let del s =
        let complete = function
            | Gray, "an" -> "a"
            | Gray, "-i" -> "-"
            | Gray, "@"  -> "@"
            | Gray, "!"  -> "!"
            | Gray, "-"  -> "-"
            | Gray, "+"  -> "+"
            | Gray, "a"  -> "a"
            | Gray, w -> ""
            | _, w -> w
        currentApply (fun (t, w) -> { s with Current = Some (t, complete (t, w.Substring(0, w.Length - 1))) }) s
    let next s =
        currentApply (fun (t, w) ->
            if w.Length > 0 then 
                { checkpoint { s with Mode = Normal }
                    with Mode = Insert; Before = s.Current.Value :: s.Before; Current = nextColor s.Current }
            else s) s
    let tag t s = currentApply (fun (_, w) -> { checkpoint s with Current = Some (t, w) }) s
    let once = (function Some (White, _) -> not !showComments | Some (Blue, _) -> not !showFormatting | _ -> false)
    let toDef = (function Some (Red, _) -> false | _ -> true)
    let toWord w = (function Some (_, w') -> w <> w' | _ -> true)
    let find dir s =
        let compare w w' = match (w, w') with (Some (_, x), Some (_, y)) -> x = y | _ -> false
        currentApply (fun (_, w) -> let s' = dir (toWord w) s in if compare s'.Current s.Current then s' else consoleBeep (); s) s
    let toggle v s = v := not !v; s
    let newline s = { s with Current = Some (Blue, "cr") } |> next |> tag Red
    let openLine s =
        let toEnd = (function Some (Blue, "cr") -> false | _ -> true)
        let s' = right toEnd state
        (if s'.Current = Some (Blue, "cr") then insert else append) s' |> newline
    let validate s =
        let validate' = function
            | _, "" -> s
            | Gray, w -> match Map.tryFind w nameInst with | Some _ -> s | None -> failwith "Invalid instruction."
            | Blue, "cr" | Blue, "." -> s
            | Blue, _ -> failwith "Invalid format word."
            | _ -> s
        currentApply validate' s
    try
        match state.Mode, key with
        | _, 'R' -> tag Red    state
        | _, 'Y' -> tag Yellow state
        | _, 'G' -> tag Green  state
        | _, 'W' -> tag White  state |> if state.Mode = Insert then id else right once
        | _, 'A' -> tag Gray   state |> if state.Mode = Insert then id else validate
        | _, 'B' -> tag Blue   state |> validate
        | Normal, 'f' -> toggle showFormatting state
        | Normal, 'c' -> toggle showComments state
        | Normal, '1' -> load 1 state
        | Normal, '2' -> load 2 state
        | Normal, '3' -> load 3 state
        | Normal, '4' -> load 4 state
        | Normal, '5' -> load 5 state
        | Normal, '6' -> load 6 state
        | Normal, '7' -> load 7 state
        | Normal, '8' -> load 8 state
        | Normal, '9' -> load 9 state
        | Normal, '0' -> load 10 state
        | Normal, 's' -> save state
        | Normal, 'h' | Normal, 'b' -> left once state
        | Normal, 'l' | Normal, 'w' | Normal, ' ' -> right once state
        | Normal, 'k' -> left toDef state
        | Normal, 'j' -> right toDef state
        | Normal, 'o' -> openLine state
        | Normal, 'x' -> delete state
        | Normal, 'X' -> deleteBack state
        | Normal, 'y' -> yank state
        | Normal, 'p' -> putAfter state
        | Normal, 'P' -> putBefore state
        | Normal, 'u' -> undo state
        | Normal, 'i' -> insert state
        | Normal, 'a' -> append state
        | Normal, '*' -> find right state
        | Normal, '#' -> find left state
        | Insert, ' ' -> validate state |> next
        | Insert, '\b' -> del state
        |      _, k when int k = 0 -> state // ignore
        |      _, k when int k = 27  (* esc *) -> validate state |> normal
        | Normal, k when int k = 18  (* ctrl-R *) -> redo state
        | Insert, k when int k = 10  (* enter *) -> validate state |> next |> newline
        | Insert, k when int k = 13  (* enter *) -> validate state |> next |> newline
        | Insert, k when int k = 8   (* backspace *) -> del state
        | Insert, k when int k = 127 (* backspace *) -> del state
        | Insert, k when int k = 9   (* tab *)   -> validate state |> next |> tag Blue |> input '.' |> next |> tag Green
        | Insert, k when Char.IsLower(k) || Char.IsDigit(k) || Char.IsSymbol(k) || Char.IsPunctuation(k) -> input k state |> validate
        |      _, k -> failwith (sprintf "Invalid key (%i)." (int k))
    with ex -> consoleBeep (); message state ex.Message

let main state =
    let noMessage s = { s with Message = "" }
    consoleInit ()
    let rec main' state = consoleRead () |> edit state |> render |> noMessage |> main'
    state |> render |> main'


edit { Block = 1; Mode = Normal; Before = []; Current = None; After = []; Clipboard = []; Undo = None; Redo = None; Message = "Welcome to colorForth" } '1' |> main
