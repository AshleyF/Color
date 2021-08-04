open System
open System.IO
open System.Threading
open Devices
open Serdes

type Cell = {
    Name      : string
    Value     : int
    Comment   : string list }

let assemble (source : Tagged list) =
    let dict = ref Map.empty
    let ip = ref 0
    let h = ref 0
    let origin = ref None
    let count = ref Int32.MaxValue
    let slot = ref -1
    let lastIp = ref 0
    let lastSlot = ref 0
    let stack = ref []
    let push v = stack := v :: !stack
    let pop () =
        match !stack with
        | v :: stack' -> stack := stack'; v
        | [] -> failwith "STACK UNDERFLOW"
    let (asm : Cell option array) = Array.create 1024 None
    let get () =
        match asm.[!ip] with
        | Some a -> a
        | None -> { Name = ""; Value = 0; Comment = [] }
    let comment c =
        let a = get ()
        asm.[!ip] <- Some { a with Comment = c :: a.Comment }
    let align () =
        if !slot <> 3 then
            ip := !h
            h := !h + 1
            slot := 3
    let packInst show i =
        if !slot = -1 then align ()
        let a = get ()
        lastIp := !ip
        lastSlot := !slot
        asm.[!ip] <- Some { a with Value = a.Value ||| ((int i) <<< (!slot * 8)) }
        if show then comment (instName.[i])
        slot := !slot - 1
    let pack = packInst true
    let pad () =
        if !slot <> 3 then
            while !slot <> -1 do pack 0x1cuy // nop
            align ()
    let isValid addr slot =
        match !origin with
        | Some _ -> slot > 0 // TODO: real address range check
        | None -> false
    let padAsNeeded addr = if not (isValid addr !slot) then pad ()
    let data n =
        let s = sprintf "%x" n
        let s' = if Char.IsDigit s.[0] then sprintf "G%s" s else sprintf "G0%s" s
        asm.[!h] <- Some { Name = ""; Value = n; Comment = [s'] }
        h := !h + 1
    let literal n =
        pack 0x08uy // @p
        data n
    let error msg =
        let a = get ()
        asm.[!ip] <- Some { a with Comment = (sprintf "MERROR: %s" msg) :: a.Comment }
    let def n =
        pad ()
        dict := Map.add n !ip !dict
        asm.[!ip] <- Some { get () with Name = n }
        match !origin with
        | None -> origin := Some !ip
        | Some _ -> () // already set
    let address cell addr (name : string) final =
        if isValid addr (!slot + 1) then
            match asm.[cell] with
            | Some a ->
                let addr' = addr - (!origin).Value
                if final then asm.[cell] <- Some { a with Value = a.Value ||| addr'; Comment = (if name.Length > 0 then sprintf "R%s" name else sprintf "A%04x" addr') :: a.Comment }
                align ()
            | None -> failwith "Address in unpacked cell."
        else error "INVALID ADDRESS"
    let addrMacro () =
        match !origin with
        | Some orig -> address !ip (pop () + orig) "" true
        | None -> failwith "Address in unpacked cell."
    let dataMacro () = pop () |> data
    let endMacro () =
        comment "Yend"
        padAsNeeded !ip 
        pack 0x02uy // jump
        address !ip (pop ()) "" true
    let call s =
        match Map.tryFind s !dict with
        | Some addr ->
            padAsNeeded addr
            pack 0x03uy // call
            address !ip addr s true
        | None -> error (sprintf "INVALID CALL (%s)" s)
    let returnMacro () =
        match asm.[!lastIp] with
        | Some a ->
            comment "Y;"
            let mask = 0xff <<< (!lastSlot * 8)
            let call = 0x03 <<< (!lastSlot * 8) // call
            if a.Value &&& mask = call then // tail call optimization
                let jump = 0x02 <<< (!lastSlot * 8) // jump
                asm.[!lastIp] <- Some { a with Value = a.Value &&& (~~~mask) ||| jump; Comment = List.map (fun w -> if w = "call" then "jump" else w) a.Comment }
            else
                packInst false 0x00uy // return
                align ()
        | None -> failwith "Invalid lastIp."
    let beginMacro () =
        pad ()
        push !ip
        comment "Ybegin"
    let forMacro () =
        comment "Yfor"
        pack 0x1duy (* push *)
        pad (); push !ip // beginMacro ()
    let nextMacro () =
        comment "Ynext"
        let a = pop ()
        if a = !ip && !slot >= 0 then pack 0x04uy // unext
        else padAsNeeded a; packInst false 0x05uy (* next *); address !ip a "" true
    let ifMacro name code =
        padAsNeeded !ip
        comment (sprintf "Y%s" name)
        pack code // if/-if
        push !ip
        align () // to be patched by 'then' or 'else'
    let elseMacro () =
        pad ()
        address (pop ()) (!ip + 1) "" true // patch 'if' to just beyond 'else'
        comment "Yelse"
        pack 0x02uy // jump
        push !ip
        align () // to be patched by 'then'
    let thenMacro () =
        pad ()
        address (pop ()) !ip "" true // patch 'if' or 'else'
        comment "Ythen"
    let loadMacro () =
        comment "Yload"
        literal 0
        pack 0x1duy // push
        pad ()
        pack 0x08uy // @p
        pack 0x0duy // !+
        pack 0x04uy // unext
        pad ()
    let initMacro () =
        comment "Yinit"
        let orig = match !origin with Some x -> x | None -> 0
        pad ()
        count := !ip - orig - 1
        let temp = !h
        h := 1 // load count
        data !count
        h := temp
    let print () =
        let print' i = function
            | Some { Name = n; Value = v; Comment = comment } ->
                let addr = match !origin with Some orig -> i - orig | None -> i
                Console.ForegroundColor <- ConsoleColor.DarkGray
                if addr < 0 || addr > !count then printf "      "
                else printf "%04x  " addr
                Console.ForegroundColor <- ConsoleColor.Red
                Console.Write("{0,10}  ", n)
                Console.ForegroundColor <- ConsoleColor.White
                printf "%02x %02x %02x %02x  " ((v &&& 0xff000000) >>> 24) ((v &&& 0xff0000) >>> 16) ((v &&& 0xff00) >>> 8) (v &&& 0xff)
                let printCommentWord (w : string) =
                    let print s c =
                        Console.ForegroundColor <- c
                        printf "%s " (w.Substring(s))
                    match w.[0] with
                    | 'M' -> print 1 ConsoleColor.Magenta
                    | 'R' -> print 1 ConsoleColor.Red
                    | 'G' -> print 1 ConsoleColor.Green
                    | 'Y' -> print 1 ConsoleColor.Yellow
                    | 'A' -> print 1 ConsoleColor.DarkGray
                    |  _  -> print 0 ConsoleColor.DarkGray
                comment |> List.rev |> List.iter printCommentWord
                Console.WriteLine()
            | None -> ()
        Array.iteri print' asm
    let save () = List.ofArray asm |> List.filter (Option.isSome) |> List.map (function Some { Value = v } -> v | None -> failwith "Should have been filtered") |> saveRaw false 0
    for t in source do
        match t with
        | Define n           -> def n
        | Execute (Number n) -> push n
        | Execute (Word w) ->
            match w with
            | "load"  -> loadMacro ()
            | "init"  -> initMacro ()
            | "data"  -> dataMacro ()
            | "align" -> align ()
            | "pad"   -> pad ()
            | "addr"  -> addrMacro ()
            | ";"     -> returnMacro ()
            | "for"   -> forMacro ()
            | "begin" -> beginMacro ()
            | "end"   -> endMacro ()
            | "next"  -> nextMacro ()
            | "if"    -> ifMacro w 0x06uy
            | "-if"   -> ifMacro w 0x07uy
            | "else"  -> elseMacro ()
            | "then"  -> thenMacro ()
            | _ -> error (sprintf "UNKNOWN IMMEDIATE WORD (%s)" w)
        | Compile (Number n) -> literal n
        | Compile (Word w)   -> call w
        | Comment _ | Format _ -> ()
        | Instruction i      -> pack i
    print ()
    save ()

let assembleBlock = ref ""
let changed (a : FileSystemEventArgs) =
    if Path.GetExtension(a.FullPath) = ".blk" then
        assembleBlock := a.FullPath
let watcher = new FileSystemWatcher(Path.GetDirectoryName(blockFile 0 "blk"))
watcher.Changed.Add(changed)
watcher.Created.Add(changed)
let rec watch () =
    watcher.WaitForChanged(WatcherChangeTypes.Changed ||| WatcherChangeTypes.Created) |> ignore
    Thread.Sleep(300) // give time for save to complete
    let block = !assembleBlock
    assembleBlock := ""
    if block.Length > 0 then
        try
            Console.Clear()
            Int32.Parse(Path.GetFileNameWithoutExtension(block)) |> loadTagged |> assemble
        with ex ->
            blockInputSelect -1
            Console.ForegroundColor <- ConsoleColor.Magenta
            printfn "Error: %s" ex.Message
    watch ()
watch ()

// TODO: is jump/call/next/if/-if necessary (e.g. unext for decrement)
// TODO: better pre-packing address size checks
