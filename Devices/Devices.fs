module Devices

open System
open System.IO

let consoleInput () = Console.ReadKey(true).Key |> int

let consoleOutput x =
    match x >>> 24 with
    | 0 -> char x |> Console.Write
    | 1 -> Console.SetCursorPosition(x &&& 0xff, (x >>> 8) &&& 0xff)
    | 2 -> Console.ForegroundColor <- enum (x &&& 0xf)
    | 3 -> Console.BackgroundColor <- enum (x &&& 0xf)

let blockIO =
    let block b = File.Open(sprintf @"..\..\..\Source\%i.blk" b, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read)
    let file = block 0
    let reader = ref (new BinaryReader(file))
    let writer = ref (new BinaryWriter(file))
    let select i =
        (!reader).Close(); (!writer).Close()
        let file = block i
        reader := new BinaryReader(file)
        writer := new BinaryWriter(file)
    let input () = (!reader).ReadInt32()
    let output (v : int32) = (!writer).Write(v); (!writer).Flush()
    select, input, output

let blockSelect, blockInput, blockOutput = blockIO