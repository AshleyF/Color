module Serdes

open Devices
open System
open System.Text
open System.Globalization

type Kind =
    | Number of int
    | Word   of string

type Tagged =
    | Define      of string
    | Execute     of Kind
    | Compile     of Kind
    | Comment     of string
    | Format      of string
    | Instruction of byte

let serialize =
    let str (s : string) =
        let pack v x = x <<< 8 ||| v
        let len = s.Length
        let header = s |> Seq.take (min 3 len) |> Seq.fold (fun h c -> pack (int c) h) 0 |> pack len // 3 chars and len
        let chars = (Seq.map (int >> pack 0xfd) >> List.ofSeq) s // char and fd
        header :: chars
    let serialize' = function
        | Define n           ->  0x40 :: str n
        | Execute (Number n) -> [0x41; n]
        | Execute (Word w)   ->  0x42 :: str w
        | Compile (Number n) -> [0x43; n]
        | Compile (Word w)   ->  0x44 :: str w
        | Format f           ->  0xfe :: str f
        | Comment c          ->  0xff :: str c
        | Instruction i      -> [int i]
    List.map serialize' >> List.concat

let deserialize =
    let sb = new StringBuilder()
    let stringOfSeq s = sb.Clear().Append(Array.ofSeq s).ToString()
    let rec deserialize' code =
        let str tag =function
            | s :: t ->
                let len = s &&& 0xff
                let w = Seq.take len t |> Seq.map (fun c -> char (c >>> 8)) |> stringOfSeq
                let t' = Seq.skip len t |> List.ofSeq
                deserialize' (tag w :: code) t'
            | _ -> failwith "Invalid serialization format (string)."
        let num tag = function n :: t -> deserialize' (tag n :: code) t | _ -> failwith "Invalid serialization format (number)."
        function
        | 0x40 :: t -> str Define t
        | 0x41 :: t -> num (Number >> Execute) t
        | 0x42 :: t -> str (Word   >> Execute) t
        | 0x43 :: t -> num (Number >> Compile) t
        | 0x44 :: t -> str (Word   >> Compile) t
        | 0xfe :: t -> str Format t
        | 0xff :: t -> str Comment t
        | i    :: t -> deserialize' (Instruction (byte i) :: code) t
        | [] -> List.rev code
    deserialize' []

let saveRaw len b (r : int list) =
    blockOutputSelect b
    if len then blockOutput (r.Length)
    List.iter blockOutput r
    blockOutputSelect -1 // close

let loadRaw b =
    blockInputSelect b
    let len = try blockInput () with _ -> 0
    let raw = [for _ in 0 .. len - 1 do yield blockInput ()]
    blockInputSelect -1 // close
    raw

let saveTagged b = serialize >> saveRaw true b

let loadTagged = loadRaw >> deserialize

let num2str n = let s = sprintf "%x" n in if Char.IsDigit s.[0] then s else sprintf "0%s" s
let str2num (s : string) =
    if s.Length > 0 && Char.IsDigit s.[0] then
        match Int32.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture) with
        | true, n -> Some n
        | false, _ -> None
    else None

let instructions =
    [";"; "ex"; "jump"; "call"; "unext"; "next"; "if"; "-if"; "@p"; "@+"; "@b"; "@"; "!p"; "!+"; "!b"; "!"
     "+*"; "2*"; "2/"; "-"; "+"; "and"; "or"; "drop"; "dup"; "pop"; "over"; "a"; "."; "push"; "b!"; "a!"
     "break"; "mark"; "print"]
    |> List.mapi (fun i n -> byte i, n)

let instName = Map.ofList instructions
let nameInst = instructions |> List.map (fun (i, s) -> s, i) |> Map.ofList

type Color = Gray | Red | Green | Yellow | White | Blue | Magenta | Black
type Token = Color * string

let tagged2token = function
    | Define n           -> Red, n
    | Execute (Number n) -> Yellow, num2str n
    | Execute (Word w)   -> Yellow, w
    | Compile (Number n) -> Green, num2str n
    | Compile (Word w)   -> Green, w
    | Comment c          -> White, c
    | Format f           -> Blue, f
    | Instruction i      -> Gray, instName.[i]

let token2tagged = function
    | Red, s -> Define s
    | Yellow, s ->
        match str2num s with
        | Some n -> Execute (Number n)
        | None -> Execute (Word s)
    | Green, s ->
        match str2num s with
        | Some n -> Compile (Number n)
        | None -> Compile (Word s)
    | Blue,  f -> Format f
    | White, s -> Comment s
    | Gray,  s -> Instruction (nameInst.[s])
    | _ -> failwith "Invalid token."

let loadTokens = loadTagged >> List.map tagged2token
let saveTokens b = List.map token2tagged >> saveTagged b

let tokens2text =
    let prefix = function
        | Gray   -> "^"
        | Red    -> ":"
        | Green  -> ""
        | Yellow -> "`"
        | White  -> "_"
        | _ -> failwith "Invalid color."
    List.map (function
        | Blue, "." -> "\t"
        | Blue, "cr" -> Environment.NewLine
        | color, token -> (prefix color) + token) >> String.concat " "
