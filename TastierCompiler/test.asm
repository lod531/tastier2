.names 13
.proc Const
.Var const0
.Var const1
.var 1 globalTest
.proc testAssignment
.proc testBooleanOps
.proc testConst
.proc testForLoop
.proc testArrays
.proc testConditionals
.proc testSwitch
.proc testStruct
.proc Main
Const: Enter 3
Const 99
StoG 4
Const 2
StoG 5
Leave
Ret
testAssignment: Enter 2
Const 0
Const 32
StoI 0
Const 1
Const 64
StoI 0
Const 0
LoadI 0
Write
Const 1
LoadI 0
Write
Const 3
Const 3
Add
Const 128
StoIG
Const 3
Const 3
Add
LoadIG
Write
Leave
Ret
testBooleanOps: Enter 2
Const 0
Const 0
StoI 0
Const 1
Const 0
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Equ
FJmp L$0
Const 0
Write
Jmp L$1
L$0: Nop
L$1: Nop
Const 1
Const 1
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Equ
FJmp L$2
Const 1
Write
Jmp L$3
L$2: Nop
L$3: Nop
Const 0
Const 0
StoI 0
Const 1
Const 1
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Leq
FJmp L$4
Const 2
Write
Jmp L$5
L$4: Nop
L$5: Nop
Const 0
Const 1
StoI 0
Const 1
Const 1
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Leq
FJmp L$6
Const 3
Write
Jmp L$7
L$6: Nop
L$7: Nop
Const 0
Const 2
StoI 0
Const 1
Const 1
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Leq
FJmp L$8
Const 4
Write
Jmp L$9
L$8: Nop
L$9: Nop
Const 0
Const 0
StoI 0
Const 1
Const 1
Neg
Const 1
Add
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Geq
FJmp L$10
Const 5
Write
Jmp L$11
L$10: Nop
L$11: Nop
Const 0
Const 0
StoI 0
Const 1
Const 0
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Geq
FJmp L$12
Const 6
Write
Jmp L$13
L$12: Nop
L$13: Nop
Const 0
Const 1
StoI 0
Const 1
Const 0
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Geq
FJmp L$14
Const 7
Write
Jmp L$15
L$14: Nop
L$15: Nop
Const 0
Const 0
StoI 0
Const 1
Const 0
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Neq
FJmp L$16
Const 8
Write
Jmp L$17
L$16: Nop
L$17: Nop
Const 0
Const 0
StoI 0
Const 1
Const 1
StoI 0
Const 0
LoadI 0
Const 1
LoadI 0
Neq
FJmp L$18
Const 9
Write
Jmp L$19
L$18: Nop
L$19: Nop
Leave
Ret
testConst: Enter 1
Const 0
Const 1
Const 3
Add
LoadIG
Const 2
Const 3
Add
LoadIG
Add
StoI 0
Const 0
LoadI 0
Write
Leave
Ret
testForLoop: Enter 1
Const 0
Const 0
StoI 0
Jmp L$20
L$21: Nop
Const 0
Const 0
LoadI 0
Const 1
Add
StoI 0
L$20: Nop
Const 0
LoadI 0
Const 10
Lss
FJmp L$22
Const 0
LoadI 0
Write
Jmp L$21
L$22: Nop
Leave
Ret
testArrays: Enter 18
Const 3
Const 2
StoI 0
Const 4
Const 2
StoI 0
Const 5
Const 3
StoI 0
Const 2
Const 0
StoI 0
Jmp L$23
L$24: Nop
Const 2
Const 2
LoadI 0
Const 1
Add
StoI 0
L$23: Nop
Const 2
LoadI 0
Const 5
LoadI 0
Lss
FJmp L$25
Const 1
Const 0
StoI 0
Jmp L$26
L$27: Nop
Const 1
Const 1
LoadI 0
Const 1
Add
StoI 0
L$26: Nop
Const 1
LoadI 0
Const 4
LoadI 0
Lss
FJmp L$28
Const 0
Const 0
StoI 0
Jmp L$29
L$30: Nop
Const 0
Const 0
LoadI 0
Const 1
Add
StoI 0
L$29: Nop
Const 0
LoadI 0
Const 3
LoadI 0
Lss
FJmp L$31
Const 6
Const 2
LoadI 0
Dup
Const 3
Gtr
FJmp L$32
ArrayOutOfBounds
L$32: Nop
Const 2
Mul
Const 2
Mul
Add
Const 1
LoadI 0
Dup
Const 2
Gtr
FJmp L$33
ArrayOutOfBounds
L$33: Nop
Const 2
Mul
Add
Const 0
LoadI 0
Dup
Const 2
Gtr
FJmp L$34
ArrayOutOfBounds
L$34: Nop
Add
Const 2
LoadI 0
Const 4
LoadI 0
Mul
Const 3
LoadI 0
Mul
Const 1
LoadI 0
Const 4
LoadI 0
Mul
Add
Const 0
LoadI 0
Add
StoI 0
Jmp L$30
L$31: Nop
Jmp L$27
L$28: Nop
Jmp L$24
L$25: Nop
Const 2
Const 0
StoI 0
Jmp L$35
L$36: Nop
Const 2
Const 2
LoadI 0
Const 1
Add
StoI 0
L$35: Nop
Const 2
LoadI 0
Const 5
LoadI 0
Lss
FJmp L$37
Const 1
Const 0
StoI 0
Jmp L$38
L$39: Nop
Const 1
Const 1
LoadI 0
Const 1
Add
StoI 0
L$38: Nop
Const 1
LoadI 0
Const 4
LoadI 0
Lss
FJmp L$40
Const 0
Const 0
StoI 0
Jmp L$41
L$42: Nop
Const 0
Const 0
LoadI 0
Const 1
Add
StoI 0
L$41: Nop
Const 0
LoadI 0
Const 3
LoadI 0
Lss
FJmp L$43
Const 6
Const 2
LoadI 0
Dup
Const 3
Gtr
FJmp L$44
ArrayOutOfBounds
L$44: Nop
Const 2
Mul
Const 2
Mul
Add
Const 1
LoadI 0
Dup
Const 2
Gtr
FJmp L$45
ArrayOutOfBounds
L$45: Nop
Const 2
Mul
Add
Const 0
LoadI 0
Dup
Const 2
Gtr
FJmp L$46
ArrayOutOfBounds
L$46: Nop
Add
LoadI 0
Write
Jmp L$42
L$43: Nop
Jmp L$39
L$40: Nop
Jmp L$36
L$37: Nop
Leave
Ret
testConditionals: Enter 2
Const 1
Const 43
StoI 0
Const 0
Const 1
LoadI 0
Const 42
Equ
FJmp L$47
Const 45
Jmp L$48
L$47: Nop
Const 50
L$48: Nop
StoI 0
Const 0
LoadI 0
Write
Leave
Ret
testSwitch: Enter 1
Const 0
Const 0
StoI 0
Jmp L$49
L$50: Nop
Const 0
Const 0
LoadI 0
Const 1
Add
StoI 0
L$49: Nop
Const 0
LoadI 0
Const 4
Lss
FJmp L$51
Const 0
LoadI 0
Dup
Const 0
Equ
FJmp L$53
Const 0
Write
Jmp L$52
L$53: Nop
Dup
Const 1
Equ
FJmp L$54
Const 1
Write
Jmp L$52
L$54: Nop
Dup
Const 2
Equ
FJmp L$55
Const 2
Write
Jmp L$52
L$55: Nop
Const 100
CharWrite
Const 101
CharWrite
Const 102
CharWrite
Const 97
CharWrite
Const 117
CharWrite
Const 108
CharWrite
Const 116
CharWrite
L$52: Nop
Pop
Jmp L$50
L$51: Nop
Leave
Ret
testStruct: Enter 2
Const 0
Const 3
StoI 0
Const 0
LoadI 0
Write
Leave
Ret
Main: Enter 0
Call 1 testAssignment
Call 1 testBooleanOps
Call 1 testConst
Call 1 testForLoop
Call 1 testArrays
Call 1 testConditionals
Call 1 testSwitch
Call 1 testStruct
Leave
Ret
