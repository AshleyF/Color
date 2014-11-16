module Machine

type Machine(input : (unit -> int) array, output : (int -> unit) array, hook, extention) =
    let high = 0x8000
    let mutable p, i, slot, t, s, si, r, ri, a, b = high, 0, 4, 0, 0, 0, 0, 0, 0, high + 1

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
            | 0x00 -> p <- popr (); fetch () // ;      "return"
            | 0x01 -> ex ()                  // ex     "execute" (swap P and R)
            | 0x02 -> jump ()                // jump
            | 0x03 -> pushr p; jump ()       // call
            | 0x04 -> next id slotzero       // unext  "micronext" (loop within I, decrement R)
            | 0x05 -> next fetch jump        // next   (loop to address, decrement R)
            | 0x06 -> cond ((=) 0)           // if     (jump if T=0)
            | 0x07 -> cond ((>=) 0)          // -if    "minus-if" (jump if T≥0)
            | 0x08 -> incp () |> fetchm      // @p     "fetch-P" (fetch inline literal via P, autoincrement)
            | 0x09 -> inca () |> fetchm      // @+     "fetch-plus" (fetch via A, autoincrement)
            | 0x0a -> fetchm b               // @b     "fetch-B" (fetch via B)
            | 0x0b -> fetchm a               // @      "fetch" (fetch via A)
            | 0x0c -> incp () |> storem      // !p     "store-P" (store via P, autoincrement)
            | 0x0d -> inca () |> storem      // !+     "store-plus" (store via A, autoincrement)
            | 0x0e -> storem b               // !b     "store-B" (store via B)
            | 0x0f -> storem a               // !      "store" (store via A)
            | 0x10 -> multiply ()            // +*     "multiply-step"
            | 0x11 -> unary (flip (<<<) 1)   // 2*     "two-star" (left shift)
            | 0x12 -> unary (flip (>>>) 1)   // 2/     "two-slash" (right shift, signed)
            | 0x13 -> unary (~~~)            // -      "not" (invert all bits)
            | 0x14 -> binary (+)             // +      "plus"
            | 0x15 -> binary (&&&)           // and
            | 0x16 -> binary (^^^)           // or     "or" (exclusive or)
            | 0x17 -> pops () |> ignore      // drop
            | 0x18 -> pushs t                // dup
            | 0x19 -> popr () |> pushs       // pop    (from R to T)
            | 0x1a -> pushs s                // over
            | 0x1b -> pushs a                // a      (A to T)
            | 0x1c -> ()                     // .      "nop"
            | 0x1d -> pops () |> pushr       // push   (from T to R)
            | 0x1e -> b <- pops ()           // b!     "B-store" (store into B)
            | 0x1f -> a <- pops ()           // a!     "A-store" (store into A)
            | x -> extention x
        prefetch ()

    member x.Run() =
        try
            while true do
                hook p i slot a b t s si stk r ri rtn ram
                step ()
        with _ -> ()
