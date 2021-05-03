// don't look too close at this! :P
// it's just a rough sketch of an idea to synthesize programs to
// accomplish small things like swap, nip, etc. constructing literals, inclusive or, etc.
// I need to think more about it and encapsulate the general synthesis and simplify things...

type State = { reg : int; ret : int list; stk : int list }
let init a r d = { reg = a; ret = r; stk = d }

type Instruction =
    | Not
    | Plus
    | And | Or
    | TwoStar | TwoSlash
    | Drop | Dup | Over
    | Push | Pop
    | Fetch | Store

let rec exec state prog =
    let unary f = function
        | x :: t -> f x @ t
        | _ -> failwith "Stack underflow"
    let binary f = function
        | x :: y :: t -> f x y @ t
        | _ -> failwith "Stack underflow"
    let unaryFix f x = [f x]
    let binaryFix f x y = [f x y]
    let stackOp f tail = exec { state with stk = f state.stk } tail
    let flip f x y = f y x
    let dup = function x :: t -> x :: x :: t | [] -> [0; 0]
    let over = function
        | x :: y :: t -> y :: x :: y :: t
        | _ -> failwith "Stack underflow"
    let push s =
        match s.stk with
        | x :: t -> { s with ret = x :: s.ret; stk = t }
        | _ -> failwith "Stack underflow"
    let pop s =
        match s.ret with
        | x :: t -> { s with stk = x :: s.stk; ret = t }
        | _ -> failwith "Stack underflow"
    let fetch s = { s with stk = s.reg :: s.stk }
    let store s =
        match s.stk with
        | x :: t -> { s with reg = x; stk = t }
        | _ -> failwith "Stack underflow"
    try
        match prog with
        | [] -> state
        | Not      :: t -> stackOp (unary  (unaryFix (~~~)))          t
        | Plus     :: t -> stackOp (binary (binaryFix (+)))           t
        | And      :: t -> stackOp (binary (binaryFix (&&&)))         t
        | Or       :: t -> stackOp (binary (binaryFix (|||)))         t
        | TwoStar  :: t -> stackOp (unary  (unaryFix (flip (<<<) 1))) t
        | TwoSlash :: t -> stackOp (unary  (unaryFix (flip (>>>) 1))) t
        | Drop     :: t -> stackOp List.tail  t
        | Dup      :: t -> stackOp dup        t
        | Over     :: t -> stackOp over       t
        | Push     :: t -> exec (push state)  t
        | Pop      :: t -> exec (pop state)   t
        | Fetch    :: t -> exec (fetch state) t
        | Store    :: t -> exec (store state) t
    with _ -> state

let correct strict expected state = // stack only
    try
        let len = List.length expected
        let result = Seq.take len state.stk |> List.ofSeq
        result = expected && (not strict || List.length state.stk = len)
    with _ -> false

let code n set =
    let rec code' n set set' = seq {
        let rec combine a b = seq { for x in a do for y in b do yield x :: y }
        if n = 1 then for x in set do yield [x] else
            let set'' = combine set set' |> List.ofSeq
            yield! set''
            yield! code' (n - 1) set set'' }
    List.map (fun x -> [x]) set |> code' n set

let search strict max instructions examples =
    let search' experiments (input, output) = seq {
        for prog in experiments do
            let result = exec input prog
            if correct strict output result then yield prog } |> Set.ofSeq
    let experiments = code max instructions
    Seq.length experiments |> printfn "Running %i experiments..."
    let solutions =
        examples
        |> Seq.map (search' experiments)
        |> Seq.fold Set.intersect (Set.ofSeq experiments)
    let shortest = Seq.minBy List.length solutions |> List.length
    solutions |> Seq.filter (fun p -> List.length p <= shortest)

printfn "Synthesis"

search
    false // strictly exact stack or only top n-values required?
    2 // max number of instructions
    [Not; Plus; And; Or; TwoStar; TwoSlash; Drop; Dup; Over; Push; Pop; Fetch; Store] // instructions to try
    [(init 0 [] [1; 2]), [1; 2; 1; 2] // examples (machine state -> stack)
     (init 0 [] [5; 7]), [5; 7; 5; 7]] // e.g. 5 7 -> 5 7 5 7 "2dup" (finds: over over)
|> Seq.iter (printfn "FOUND: %A")
