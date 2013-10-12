module Console

open System
open Serdes

// buffering console wrappers

let width, height = 80, 40

let color = function
    | Red     -> ConsoleColor.Red
    | Yellow  -> ConsoleColor.Yellow
    | Green   -> ConsoleColor.Green
    | White   -> ConsoleColor.White
    | Blue    -> ConsoleColor.Blue
    | Gray    -> ConsoleColor.DarkGray
    | Magenta -> ConsoleColor.Magenta
    | Black   -> ConsoleColor.Black

let empty () = Array2D.create width height (White, Black, ' ')
let current = ref (empty ())
let last = ref (empty ())
let x, y = ref 0, ref 0

let consoleBeep () = Console.Beep()
let consoleRead () = Console.ReadKey(true).KeyChar

let consoleSetX x' = x := x'
let consoleSetY y' = y := y'
let consoleSetXY x' y' = consoleSetX x'; consoleSetY y'

let consoleClear () = // Console.Clear()
    current := empty ()
    consoleSetXY 0 0

let consoleWriteLine () = // Console.WriteLine()
    min (height - 1) (!y + 1) |> consoleSetXY 0

let consoleWrite f b (s : string) =
    let write c =
        (!current).[!x, !y] <- (f, b, c)
        !x + 1 |> consoleSetX
        if !x = width then consoleWriteLine ()
    Seq.iter write s

let consoleWriteStatus (s : string) =
    height - 3 |> consoleSetXY 0 // Console.SetCursorPosition(0, height - 3)
    consoleWrite Magenta Black (s.Substring(0, min (s.Length) (width - 1)))

let consoleRefresh () =
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
    Console.SetCursorPosition(!x, !y)

let consoleInit () =
    Console.CursorVisible <- false
    Console.SetWindowSize(width, height)
    Console.SetBufferSize(width, height)

// TODO: ANSI commands for positioning and colors instead of .NET Console.* APIs on Linux