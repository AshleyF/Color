# Tricks of the Trade

TODO: Document colorForth tricks

http://www.colorforth.com/inst.htm

## Computed Control Flow

`push ;`
`push ex`

## Co-routine Jump

## Shift

`for 2* unext`
`for 2/ unext`

## Move Data

`for @+ !b unext`
`for @b !+ unext`

## Mod

`/mod for begin over over . + -if drop 2* swap next ; then over or or - 2* - next ;`

## Multiply Tricks

* Shift 1 into bit 0: `- 2* -`
* Multiply by 3: `dup 2* . +`
* Multiply by 5: `dup 2* 2* . +`
* Multiply by 6: `2* dup 2* . +`
* Multiply by 7: `dup 2* dup 2* .+ .+`

## Construct Literals

* Construct 0: `dup or` (trash) or `dup dup or`
* Construct -1: `dup or -`
* Construct -2: `dup or - 2*`
* Construct +1 (same number of slots as literal): `dup or - 2* -`

## Miscellaneous

* Test for positive: `- -if`
* Negate: `- 1 . +`
* Subtract S from T: `- . + -`
* Subtract T from S: `push - pop . + -`
* Inclusive-or: `over - and or`
* Nip: `over or or` or `push drop pop`
* Swap: `over push over or or pop` or `over push push drop pop pop` or `push a! pop a`
* Retrieve loop index: `pop dup push`
* Discard loop index and exit loop: `pop drop ;`
* Discard return address (return to caller's caller): `pop drop ;`
* 2dup: `over over`
