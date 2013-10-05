type Machine(input : (unit -> int) array, output : (int -> unit) array, hook, extention) =
    let high = 0x8000
    let mutable p, i, slot, t, s, si, r, ri, a, b = high, 0, 4, 0, 0, 0, 0, 0, 0, high

    let stk, rtn = Array.zeroCreate 8, Array.zeroCreate 8
    let move x d = (x + d) &&& 0b111
    let pushr v = rtn.[ri] <- r; ri <- move ri 1; r <- v
    let popr () = ri <- move ri -1; let x = r in r <- rtn.[ri]; x
    let pushs v = stk.[si] <- s; si <- move si 1; s <- t; t <- v
    let pops () = let x = t in t <- s; si <- move si -1; s <- stk.[si]; x

    let ram = Array.zeroCreate 8192
    let mem x = x &&& high <> high
    let get x = if mem x then ram.[x] else input.[x ^^^ high] ()
    let set x v = if mem x then ram.[x] <- v else output.[x ^^^ high] (v)
    let fetchm x = get x |> int |> pushs
    let storem x = pops () |> set x

    let unary f = t <- f t
    let binary f = t <- pops () |> f t
    let flip f a b = f b a
    let incp () = let p' = p in (if mem p then p <- p + 1); p'
    let inca () = let a' = a in (if mem a then a <- a + 1); a'

    let step () =
        let fetch () = i <- incp() |> get; slot <- 0
        let prefetch () = if slot = 4 then fetch ()
        let decode () = prefetch (); slot <- slot + 1; (i >>> (32 - slot * 8)) &&& 0xff
        let slotzero () = slot <- 0
        let setp x = p <- x; slotzero (); fetch ()
        let jump () = let m = (0xffffff >>> (slot - 1) * 8) in (p &&& (~~~m)) ||| (i &&& m) |> setp
        let ex () = let x = r in r <- p; setp x
        let next f g = if r = 0 then popr () |> ignore; f () else r <- r - 1; g ()
        let multiply () =
            if (a &&& 1) = 1 then t <- t + s
            let x = t &&& 1 in t <- t >>> 1; a <- ((uint32 a) >>> 1) ||| ((uint32 x) <<< 31) |> int
        let cond f = if f t then jump () else fetch ()
        match decode () with
            | 0x00 -> p <- popr (); fetch () // ; (return)
            | 0x01 -> ex ()                  // ex (execute)
            | 0x02 -> jump ()                // name ; (jump)
            | 0x03 -> pushr p; jump ()       // name (call)
            | 0x04 -> next id slotzero       // unext (micronext)
            | 0x05 -> next fetch jump        // next
            | 0x06 -> cond ((=) 0)           // if
            | 0x07 -> cond ((<=) 0)          // -if (minus-if)
            | 0x08 -> incp () |> fetchm      // @p (fetch-P)
            | 0x09 -> inca () |> fetchm      // @+ (fetch-plus)
            | 0x0a -> fetchm b               // @b (fetch-B)
            | 0x0b -> fetchm a               // @ (fetch)
            | 0x0c -> incp () |> storem      // !p (store-P)
            | 0x0d -> inca () |> storem      // !+ (store-plus)
            | 0x0e -> storem b               // !b (store-B)
            | 0x0f -> storem a               // ! (store)
            | 0x10 -> multiply ()            // +* (multiply-step)
            | 0x11 -> unary (flip (<<<) 1)   // 2* (two-star)
            | 0x12 -> unary (flip (>>>) 1)   // 2/ (two-slash)
            | 0x13 -> unary (~~~)            // - (not)
            | 0x14 -> binary (+)             // + (plus)
            | 0x15 -> binary (&&&)           // and
            | 0x16 -> binary (^^^)           // or (exclusive or)
            | 0x17 -> pops () |> ignore      // drop
            | 0x18 -> pushs t                // dup
            | 0x19 -> popr () |> pushs       // pop
            | 0x1a -> pushs s                // over
            | 0x1b -> pushs a                // a
            | 0x1c -> ()                     // . (nop)
            | 0x1d -> pops () |> pushr       // push
            | 0x1e -> b <- pops ()           // b! (B-store)
            | 0x1f -> a <- pops ()           // a! (A-store)
            | x -> extention x
        prefetch ()

    member x.Run() =
        try
            while true do
                step ()
                hook p i slot a b t s si stk r ri rtn ram
        with _ -> ()

// visualization and debugger

open Console
open Serdes
open Devices

let mutable debug = false
let mutable last = 0, 0, 0, 0, 0, 0, 0, 0, 0
let mutable origin = 0

let breakpoint = function 0x20 -> debug <- true

let visualize p i slot a b t s si (stk : int array) r ri (rtn : int array) (ram : int array) =
    if debug then
        consoleWrite Red Black "Debug Break"; consoleWriteLine ()
        consoleWrite Gray Black "- Step      <space>"; consoleWriteLine ()
        consoleWrite Gray Black "- Continue  <enter>"; consoleWriteLine ()
        consoleRefresh ()
        match consoleRead () with
        | k when int k = 13 (* enter *) -> consoleClear (); consoleRefresh (); debug <- false; ()
        | _ ->
            let p', i', a', b', t', s', si', r', ri' = last
            last <- p, i, a, b, t, s, si, r, ri
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
                    consoleSetXY 0 x
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
                    consoleWrite (if i = i' then White else Yellow) (if current then Red else Black) (sprintf "%02x" inst)
                write Gray (sprintf " %s" name)
            let indent = 18
            let displayStacks () =
                let disp ind (s : int array) x i i' =
                    consoleSetX (indent + ind)
                    sprintf "%08x" s.[x] |> consoleWrite (if x = i' && i <> i' then Yellow else White) (if i = x then Blue else Black)
                for x in 7 .. -1 .. 0 do disp 2  stk x si si'; disp 16 rtn x ri ri'; line ()
            consoleClear ()
            consoleSetX indent; display "P" p p'; space (); displayI (); line2 ()
            consoleSetX indent; display "A" a a'; space (); display "B" b b'; line2 ()
            consoleSetX indent; display "T" t t'; line ()
            consoleSetX indent; display "S" s s'; space (); display "R" r r'; line2 ()
            displayStacks ()
            displayRAM ()
            consoleRefresh ()

(new Machine([|blockInput; consoleInputBlocking; consoleInputNonBlocking|], [|consoleOutput; blockOutput; blockInputSelect; blockOutputSelect|], visualize, breakpoint)).Run()
System.Console.ReadLine() |> ignore