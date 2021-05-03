module Devices

open System
open System.IO

let consoleInputBlocking () = Console.ReadKey(true).Key |> int
let consoleInputNonBlocking () = if Console.KeyAvailable then consoleInputBlocking () else 0

let consoleOutput x =
    match x >>> 24 with
    | 0 -> char x |> Console.Write
    | 1 -> Console.SetCursorPosition(x &&& 0xff, (x >>> 8) &&& 0xff)
    | 2 -> Console.ForegroundColor <- enum (x &&& 0xf)
    | 3 -> Console.BackgroundColor <- enum (x &&& 0xf)
    | _ -> failwith "Invalid console output."

let blockFile = sprintf @"../../Blocks/%i.%s"

let blockIO =
    let block m b = File.Open(blockFile b "blk", m, FileAccess.ReadWrite, FileShare.Read)
    let (reader : BinaryReader option ref) = ref None
    let (writer : BinaryWriter option ref) = ref None
    let selectIn i =
        match !reader with Some r -> r.Close() | None -> ()
        if i >= 0 then reader := new BinaryReader(block FileMode.OpenOrCreate i) |> Some
    selectIn 0
    let selectOut i =
        match !writer with Some w -> w.Close() | None -> ()
        if i >= 0 then writer := new BinaryWriter(block FileMode.Create i) |> Some
    let input () = match !reader with Some r -> r.ReadInt32() | None -> failwith "No input block selected."
    let output (v : int32) = match !writer with Some w -> w.Write(v); w.Flush() | None -> failwith "No output block selected."
    selectIn, input, selectOut, output

let blockInputSelect, blockInput, blockOutputSelect, blockOutput = blockIO
