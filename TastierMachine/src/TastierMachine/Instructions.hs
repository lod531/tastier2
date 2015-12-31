module TastierMachine.Instructions where
import Data.Int (Int8, Int16)
import Data.List (elem)

data Instruction = Add
                 | Sub
                 | Mul
                 | Div
                 | Equ
                 | Lss
                 | Gtr
                 | Neg
                 | Leq 
                 | Geq
                 | Neq
                 | Load
		 | LoadI
                 | Sto
		 | StoI
                 | Call
                 | LoadG
		 | LoadIG
                 | StoG
		 | StoIG
                 | Const
                 | Enter
                 | Jmp
                 | FJmp
                 | Ret
                 | Leave
                 | Read
                 | Write
                 | Halt
                 | Dup
                 | Nop
		 | Pop
                 deriving (Eq, Ord, Show, Enum)

data InstructionWord = Nullary Instruction
                     | Unary Instruction Int16
                     | Binary Instruction Int16 Int16
                     deriving (Eq, Show)

arguments :: Instruction -> Int
arguments i =
  if i `elem` [Load, Sto, Call] then 2
  else if i `elem` [LoadG, StoG, Const, Enter, Jmp, FJmp, LoadI, StoI] then 1
  else 0
