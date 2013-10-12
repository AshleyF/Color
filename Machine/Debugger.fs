module Debugger

open Console
open Serdes
open Devices
open Machine

let mutable debug = false
let mutable last = 0, 0, 0, 0, 0, 0, 0, 0, 0
let mutable origin = 0
let mutable instructions, time = 0, 0

let breakpoint = function 0x20 -> debug <- true

let debugger p i slot a b t s si (stk : int array) r ri (rtn : int array) (ram : int array) =
    let timeInstruction = function
        | _ -> time <- time + 15
    let p', i', a', b', t', s', si', r', ri' = last
    last <- p, i, a, b, t, s, si, r, ri
    if debug then
        let write c = consoleWrite c Black
        let space () = write Black "    "
        let line = consoleWriteLine
        let line2 () = line (); line ()
        let display name reg reg' =
            sprintf "%s " name |> write Green; sprintf "%08x" reg |> write (if reg = reg' then White else Yellow)
        let displayRAM () =
            let num = 14
            let buf = 3
            if p < 0x8000 then
                if p < origin + buf then origin <- max 0 (p - num + buf)
                elif p > origin + num - buf then origin <- min 0x7fff (p - buf)
            for x in 0 .. num do
                x + 2 |> consoleSetXY 0
                let addr = origin + x
                sprintf "%04x  " addr |> write Gray
                sprintf "%08x" ram.[addr] |> consoleWrite White (if addr = p then Red else Black)
        let displayI () =
            write Green "I "
            let mutable name = "???"
            for x in 24 .. -8 .. 0 do
                let current = 24 - slot * 8 = x
                let inst = byte ((i >>> x) &&& 0xff)
                if current then
                    match Map.tryFind inst instName with
                    | Some n -> name <- n
                    | None -> ()
                    timeInstruction inst
                consoleWrite (if i = i' then White else Yellow) (if current then Red else Black) (sprintf "%02x" inst)
            write Gray (sprintf " %s" name)
        let indent = 18
        let displayStacks () =
            let disp ind (s : int array) x i i' =
                consoleSetX (indent + ind)
                sprintf "%08x" s.[x] |> consoleWrite (if x = i' && i <> i' then Yellow else White) (if i = x then Blue else Black)
            for x in 7 .. -1 .. 0 do disp 2  stk x si si'; disp 16 rtn x ri ri'; line ()
        let displayTime () =
            write Green "Time "; sprintf "%0.1fns" ((float time) / 10.) |> write White
        consoleClear ()
        write Red "Debug  "
        write Green "Instructions "; sprintf "%i  " instructions |> write White
        displayTime ()
        line2 ()
        instructions <- instructions + 1
        consoleSetX indent; display "P" p p'; space (); displayI (); line2 ()
        consoleSetX indent; display "A" a a'; space (); display "B" b b'; line2 ()
        consoleSetX indent; display "T" t t'; line ()
        consoleSetX indent; display "S" s s'; space (); display "R" r r'; line2 ()
        displayStacks ()
        displayRAM ()
        consoleRefresh ()
        match consoleRead () with
        | k when int k = 13 (* enter *) ->
            consoleClear ()
            consoleRefresh ()
            debug <- false; ()
        | _ -> ()

(new Machine([|blockInput; consoleInputBlocking; consoleInputNonBlocking|], [|blockOutput; consoleOutput; blockInputSelect; blockOutputSelect|], debugger, breakpoint)).Run()
System.Console.ReadLine() |> ignore