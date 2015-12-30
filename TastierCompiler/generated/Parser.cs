using System.Collections;


using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using Symbol = System.Tuple<string, int, int, int, int, int[]>;
using Instruction = System.Tuple<string,string>;
using StackAddress = System.Tuple<int, int>;

namespace Tastier {



public class Parser {
	public const int _EOF = 0;
	public const int _ident = 1;
	public const int _number = 2;
	public const int maxT = 41;

	const bool T = true;
	const bool x = false;
	const int minErrDist = 2;

	public Scanner scanner;
	public Errors  errors;

	public Token t;    // last recognized token
	public Token la;   // lookahead token
	int errDist = minErrDist;

enum TastierType : int {   // types for variables
    Undefined,
    Integer,
    Boolean
  };

  enum TastierKind : int {  // kinds of symbol
    Var,
    Proc,
    Const,
    Array
  };

/*
  You'll notice some type aliases, such as the one just below, are commented
  out. This is because C# only allows using-alias-directives outside of a
  class, while class-inheritance directives are allowed inside. So the
  snippet immediately below is illegal in here. To complicate matters
  further, the C# runtime does not properly handle class-inheritance
  directives for Tuples (it forces you to write some useless methods). For
  these reasons, the type aliases which alias Tuples can be found in
  Parser.frame, but they're documented in this file, with the rest.
*/

  //using Symbol = System.Tuple<string, int, int, int, int, int[]>;

/*
  A Symbol is a name with a type and a kind. The first int in the
  tuple is the kind, and the second int is the type. We'll use these to
  represent declared names in the program.

  For each Symbol which is a variable, we have to allocate some storage, so
  the variable lives at some address in memory. The address of a variable on
  the stack at runtime has two components. The first component is which
  stack frame it's in, relative to the current procedure. If the variable is
  declared in the procedure that's currently executing, then it will be in
  that procedure's stack frame. If it's declared in the procedure that
  called the currently active one, then it'll be in the caller's stack
  frame, and so on. The first component is the offset that says how many
  frames up the chain of procedure calls to look for the variable. The
  second component is simply the location of the variable in the stack frame
  where it lives.

  The third int in the symbol is the stack frame on which the variable
  lives, and the fourth int is the index in that stack frame. Since
  variables which are declared in the global scope aren't inside any
  function, they don't have a stack frame to go into. In this compiler, our
  convention is to put these variables at an address in the data memory. If
  the variable was declared in the global scope, the fourth field in the
  Symbol will be zero, and we know that the next field is an address in
  global memory, not on the stack.

  Procedures, on the other hand, are just sets of instructions. A procedure
  is not data, so it isn't stored on the stack or in memory, but is just a
  particular part of the list of instructions in the program being run. If
  the symbol is the name of a procedure, we'll store a -1 in the address
  field (5).

  When the program is being run, the code will be loaded into the machine's
  instruction memory, and the procedure will have an address there. However,
  it's easier for us to just give the procedure a unique label, instead of
  remembering what address it lives at. The assembler will take care of
  converting the label into an address when it encounters a JMP, FJMP or
  CALL instruction with that label as a target.

  To summarize:
    * Symbol.Item1 -> name
    * Symbol.Item2 -> kind
    * Symbol.Item3 -> type
    * Symbol.Item4 -> stack frame pointer
    * Symbol.Item5 -> variable's address in the stack frame pointed to by
                      Item4, -1 if procedure
    * Symbol.Item6 -> Structre and size of symbol on stack
*/

  class Scope : ArrayList {}

/*
  A scope contains a stack of symbol definitions. Every time we come across
  a new local variable declaration, we can just push it onto the stack. We'll
  use the position of the variable in the stack to represent its address in
  the stack frame of the procedure in which it is defined. In other words, the
  variable at the bottom of the stack goes at location 0 in the stack frame,
  the next variable at location 1, and so on.
*/

  //using Instruction = Tuple<string, string>;
  class Program : List<Instruction> {}

/*
  A program is just a list of instructions. When the program is loaded into
  the machine's instruction memory, the instructions will be laid out in the
  same order that they appear in this list. Because of this, we can use the
  location of an instruction in the list as its address in instruction memory.
  Labels are just names for particular locations in the list of instructions
  that make up the program.

  The first component of all instructions is a label, which can be empty.
  The second component is the actual instruction itself.

  To summarize:
    * Instruction.Item1 -> label
    * Instruction.Item2 -> the actual instruction, as a string
*/

Stack<Scope> openScopes = new Stack<Scope>();
Scope externalDeclarations = new Scope();

/*
  Every time we encounter a new procedure declaration in the program, we want
  to make sure that expressions inside the procedure see all of the variables
  that were in scope at the point where the procedure was defined. We also
  want to make sure that expressions outside the procedure do not see the
  procedure's local variables. Every time we encounter a procedure, we'll push
  a new scope on the stack of open scopes. When the procedure ends, we can pop
  it off and continue, knowing that the local variables defined in the
  procedure cannot be seen outside, since we've popped the scope which
  contains them off the stack.
*/

Program program = new Program();
Program header = new Program();

Stack<string> openProcedureDeclarations = new Stack<string>();

/*
  In order to implement the "shadowing" of global procedures by local procedures
  properly, we need to generate a label for local procedures that is different
  from the label given to procedures of the same name in outer scopes. See the
  test case program "procedure-label-shadowing.TAS" for an example of why this
  is important. In order to make labels unique, when we encounter a non-global
  procedure declaration called "foo" (for example), we'll give it the label
  "enclosingProcedureName$foo" for all enclosing procedures. So if it's at
  nesting level 2, it'll get the label "outermost$nextoutermost$foo". Let's
  make a function that does this label generation given the set of open
  procedures which enclose some new procedure name.
*/

string generateProcedureName(string name) {
  if (openProcedureDeclarations.Count == 0) {
    return name;
  } else {
    string temp = name;
    foreach (string s in openProcedureDeclarations) {
      temp = s + "$" + temp;
    }
    return temp;
  }
}

/*
  We also need a function that figures out, when we call a procedure from some
  scope, what label to call. This is where we actually implement the shadowing;
  the innermost procedure with that name should be called, so we have to figure
  out what the label for that procedure is.
*/

string getLabelForProcedureName(int lexicalLevelDifference, string name) {
  /*
     We want to skip <lexicalLevelDifference> labels backwards, but compose
     a label that incorporates the names of all the enclosing procedures up
     to that point. A lexical level difference of zero indicates a procedure
     defined in the current scope; a difference of 1 indicates a procedure
     defined in the enclosing scope, and so on.
  */
  int numOpenProcedures = openProcedureDeclarations.Count;
  int numNamesToUse = (numOpenProcedures - lexicalLevelDifference);
  string theLabel = name;

  /*
    We need to concatenate the first <numNamesToUse> labels with a "$" to
    get the name of the label we need to call.
  */

  var names = openProcedureDeclarations.Take(numNamesToUse);

  foreach (string s in names) {
      theLabel = s + "$" + theLabel;
  }

  return theLabel;
}

Stack<string> openLabels = new Stack<string>();
int labelSeed = 0;

string generateLabel() {
  return "L$"+labelSeed++;
}

/*
  Sometimes, we need to jump over a block of code which we're about to
  generate (for example, at the start of a loop, if the test fails, we have
  to jump to the end of the loop). Because it hasn't been generated yet, we
  don't know how long it will be (in the case of the loop, we don't know how
  many instructions will be in the loop body until we actually generate the
  code, and count them). In this case, we can make up a new label for "the
  end of the loop" and emit a jump to that label. When we get to the end of
  the loop, we can put the label in, so that the jump will go to the
  labelled location. Since we can have loops within loops, we need to keep
  track of which label is the one that we are currently trying to jump to,
  and we need to make sure they go in the right order. We'll use a stack to
  store the labels for all of the forward jumps which are active. Every time
  we need to do a forward jump, we'll generate a label, emit a jump to that
  label, and push it on the stack. When we get to the end of the loop, we'll
  put the label in, and pop it off the stack.
*/

Symbol _lookup(Scope scope, string name) {
  foreach (Symbol s in scope) {
      if (s.Item1 == name) {
        return s;
      }
  }
  return null;
}

Symbol lookup(Stack<Scope> scopes, string name) {
  int stackFrameOffset = 0;
  int variableOffset = 0;

  foreach (Scope scope in scopes) {
    foreach (Symbol s in scope) {
      if (s.Item1 == name) {
        return s;
      }
      else {
        variableOffset += 1;
      }
    }
    stackFrameOffset += 1;
    variableOffset = 0;
  }
  return null; // if the name wasn't found in any open scopes.
}

/*
  You may notice that when we use a LoadG or StoG instruction, we add 3 to
  the address of the item being loaded or stored. This is because the
  control and status registers of the machine are mapped in at addresses 0,
  1, and 2 in data memory, so we cannot use those locations for storing
  variables. If you want to load rtp, rbp, or rpc onto the stack to
  manipulate them, you can LoadG and StoG to those locations.
*/

void printSymbol(Symbol sym)
{
	Console.Write("A new symbol was added to the table of kind ");
	string kindString;
	switch((int)sym.Item2)
	{
		case ((int)TastierKind.Proc):
			kindString = "procedure";
			break;
		case ((int)TastierKind.Var):
			kindString = "variable";
			break;
		case ((int)TastierKind.Const):
			kindString = "constant";
			break;
		default:
			kindString = "oh man I have no idea";
			break;
	}
	Console.Write(kindString + " ");
	if((int)sym.Item2 == (int)TastierKind.Var || (int)sym.Item2 == (int)TastierKind.Const)
	{
		Console.Write("which is a ");
		if(sym.Item4 == 0)
		{
			Console.Write("global ");
		}
		else
		{
			Console.Write("local ");
		}
		Console.Write(kindString + " with address " + sym.Item5 + " ");
	}
	Console.Write("which is named " + sym.Item1 + ".\n\n");
}


int getFrameAddress(Scope currentScope)
{
	int result = 0;
	foreach (Symbol currentSymbol in currentScope)
	{
		int[] temp = currentSymbol.Item6;
		int currentSymbolSize = 1;
		foreach(int currentDimension in temp)
		{
			currentSymbolSize *= currentDimension;
		}
		result += currentSymbolSize;
	}
	return result;

}
/*--------------------------------------------------------------------------*/



	public Parser(Scanner scanner) {
		this.scanner = scanner;
		errors = new Errors();
	}

	void SynErr (int n) {
		if (errDist >= minErrDist) errors.SynErr(la.line, la.col, n);
		errDist = 0;
	}

	public void SemErr (string msg) {
		if (errDist >= minErrDist) errors.SemErr(t.line, t.col, msg);
		errDist = 0;
	}

  public void Warn (string msg) {
    Console.WriteLine("-- line " + t.line + " col " + t.col + ": " + msg);
  }

  public void Write (string filename) {
    List<string> output = new List<string>();
    foreach (Instruction i in header) {
      if (i.Item1 != "") {
        output.Add(i.Item1 + ": " + i.Item2);
      } else {
        output.Add(i.Item2);
      }
    }
    File.WriteAllLines(filename, output.ToArray());
  }

	void Get () {
		for (;;) {
			t = la;
			la = scanner.Scan();
			if (la.kind <= maxT) { ++errDist; break; }

			la = t;
		}
	}

	void Expect (int n) {
		if (la.kind==n) Get(); else { SynErr(n); }
	}

	bool StartOf (int s) {
		return set[s, la.kind];
	}

	void ExpectWeak (int n, int follow) {
		if (la.kind == n) Get();
		else {
			SynErr(n);
			while (!StartOf(follow)) Get();
		}
	}


	bool WeakSeparator(int n, int syFol, int repFol) {
		int kind = la.kind;
		if (kind == n) {Get(); return true;}
		else if (StartOf(repFol)) {return false;}
		else {
			SynErr(n);
			while (!(set[syFol, kind] || set[repFol, kind] || set[0, kind])) {
				Get();
				kind = la.kind;
			}
			return StartOf(syFol);
		}
	}


	void AddOp(out Instruction inst) {
		inst = new Instruction("", "Add"); 
		if (la.kind == 3) {
			Get();
		} else if (la.kind == 4) {
			Get();
			inst = new Instruction("", "Sub"); 
		} else SynErr(42);
	}

	void Expr(out TastierType type) {
		TastierType type1; Instruction inst; 
		SimExpr(out type);
		if (StartOf(1)) {
			RelOp(out inst);
			SimExpr(out type1);
			if (type != type1) {
			 SemErr("incompatible types");
			}
			else {
			 program.Add(inst);
			 type = TastierType.Boolean;
			}
			
		}
	}

	void SimExpr(out TastierType type) {
		TastierType type1; Instruction inst; 
		Term(out type);
		while (la.kind == 3 || la.kind == 4) {
			AddOp(out inst);
			Term(out type1);
			if (type != TastierType.Integer || type1 != TastierType.Integer) {
			 SemErr("integer type expected");
			}
			program.Add(inst);
			
		}
	}

	void RelOp(out Instruction inst) {
		inst = new Instruction("", "Equ"); 
		switch (la.kind) {
		case 16: {
			Get();
			break;
		}
		case 17: {
			Get();
			inst = new Instruction("", "Lss"); 
			break;
		}
		case 18: {
			Get();
			inst = new Instruction("", "Gtr"); 
			break;
		}
		case 19: {
			Get();
			inst = new Instruction("", "Leq"); 
			break;
		}
		case 20: {
			Get();
			inst = new Instruction("", "Geq"); 
			break;
		}
		case 21: {
			Get();
			inst = new Instruction("", "Neq"); 
			break;
		}
		default: SynErr(43); break;
		}
	}

	void Factor(out TastierType type) {
		int n; Symbol sym; string name; int numberOfArrayIndexes = 0; 
		type = TastierType.Undefined; 
		if (la.kind == 1) {
			Ident(out name);
			bool isExternal = false; //CS3071 students can ignore external declarations, since they only deal with compilation of single files.
			
			sym = lookup(openScopes, name);
			if (sym == null) {
			 	sym = _lookup(externalDeclarations, name);
			 		isExternal = true;
			}
			
			if (sym == null) {
			 		SemErr("reference to undefined variable " + name);
			}
			program.Add(new Instruction("", "Const " + (sym.Item5)));
			
			while (la.kind == 5) {
				Get();
				Expr(out type);
				if(type != TastierType.Integer)
				{
				SemErr("non-integer index into array");
				}
				numberOfArrayIndexes++;
				if(numberOfArrayIndexes > sym.Item6.Length)
				{
				SemErr("Attempted to index into non-existent array dimension");
				}
				for(int i = 0; i < sym.Item6.Length - numberOfArrayIndexes - 1; i++)
				{
				program.Add(new Instruction("", "Const " + sym.Item6[i]));
				program.Add(new Instruction("", "Mul")); 
				}
				program.Add(new Instruction("", "Add"));
				
				Expect(6);
			}
			type = (TastierType)sym.Item3;
			if ((TastierKind)sym.Item2 == TastierKind.Var || (TastierKind)sym.Item2 == TastierKind.Const || (TastierKind)sym.Item2 == TastierKind.Array) {
			 if (sym.Item4 == 0) {
			
			     if (isExternal) {
			       program.Add(new Instruction("", "LoadIG"));
			       // if the symbol is external, we load it by name. The linker will resolve the name to an address.
			     } else {
			program.Add(new Instruction("", "Const " + 3));
			program.Add(new Instruction("", "Add"));
			       program.Add(new Instruction("", "LoadIG"));
			     }
			 } else {
			     int lexicalLevelDifference = Math.Abs(openScopes.Count - sym.Item4)-1;
			     program.Add(new Instruction("", "LoadI " + lexicalLevelDifference));
			 }
			} else SemErr("variable, constant or array identifier expected");
			
		} else if (la.kind == 2) {
			Get();
			n = Convert.ToInt32(t.val);
			program.Add(new Instruction("", "Const " + n));
			type = TastierType.Integer;
			
		} else if (la.kind == 4) {
			Get();
			Factor(out type);
			if (type != TastierType.Integer) {
			 SemErr("integer type expected");
			 type = TastierType.Integer;
			}
			program.Add(new Instruction("", "Neg"));
			program.Add(new Instruction("", "Const 1"));
			program.Add(new Instruction("", "Add"));
			
		} else if (la.kind == 7) {
			Get();
			program.Add(new Instruction("", "Const " + 1)); type = TastierType.Boolean; 
		} else if (la.kind == 8) {
			Get();
			program.Add(new Instruction("", "Const " + 0)); type = TastierType.Boolean; 
		} else SynErr(44);
	}

	void Ident(out string name) {
		Expect(1);
		name = t.val; 
	}

	void MulOp(out Instruction inst) {
		inst = new Instruction("", "Mul"); 
		if (la.kind == 9) {
			Get();
		} else if (la.kind == 10) {
			Get();
			inst = new Instruction("", "Div"); 
		} else SynErr(45);
	}

	void ProcDecl() {
		string name; string label; Scope currentScope = openScopes.Peek(); int enterInstLocation = 0; bool external = false; 
		Expect(11);
		Ident(out name);
		Symbol sym = new Symbol(name, (int)TastierKind.Proc, (int)TastierType.Undefined, openScopes.Count, -1, new int[] {1});
		                         currentScope.Add(sym);
		printSymbol(sym);
		                         openScopes.Push(new Scope());
		                         currentScope = openScopes.Peek();
		                     
		Expect(12);
		Expect(13);
		Expect(14);
		program.Add(new Instruction("", "Enter 0"));
		enterInstLocation = program.Count - 1;
		label = generateProcedureName(name);
		openProcedureDeclarations.Push(name);
		/*
		 Enter is supposed to have as an
		 argument the next free address on the
		 stack, but until we know how many
		 local variables are in this procedure,
		 we don't know what that is. We'll keep
		 track of where we put the Enter
		 instruction in the program so that
		 later, when we know how many spaces on
		 the stack have been allocated, we can
		 put the right value in.
		*/
		
		while (StartOf(2)) {
			if (la.kind == 34 || la.kind == 35) {
				VarDecl(external);
			} else if (StartOf(3)) {
				Stat();
			} else {
				openLabels.Push(generateLabel());
				program.Add(new Instruction("", "Jmp " + openLabels.Peek()));
				/*
				 We need to jump over procedure
				 definitions because otherwise we'll
				 execute all the code inside them!
				 Procedures should only be entered via
				 a Call instruction.
				*/
				
				ProcDecl();
				program.Add(new Instruction(openLabels.Pop(), "Nop")); 
			}
		}
		Expect(15);
		program.Add(new Instruction("", "Leave"));
		program.Add(new Instruction("", "Ret"));
		openScopes.Pop();
		// now we can generate the Enter instruction properly
		program[enterInstLocation] =
		 new Instruction(label, "Enter " + getFrameAddress(currentScope));
		                                   openProcedureDeclarations.Pop();
		
	}

	void VarDecl(bool external) {
		string name; TastierType type; Scope currentScope = openScopes.Peek();
		
		Type(out type);
		Ident(out name);
		Symbol sym; 
		                              if (external) {
		  	sym = new Symbol(name, (int)TastierKind.Var, (int)type, 0, 0, new int[] {1});
		                                externalDeclarations.Add(sym);
		    	printSymbol(sym);
		
		                              } else {
		  	sym = new Symbol(name, (int)TastierKind.Var, (int)type, openScopes.Count-1, getFrameAddress(currentScope), new int[] {1});
		                                currentScope.Add(sym);
		    	printSymbol(sym);
		                              }
		                          
		while (la.kind == 36) {
			Get();
			Ident(out name);
			if (external) {
			sym = new Symbol(name, (int)TastierKind.Var, (int)type, 0, 0, new int[] {1});
			printSymbol(sym);
			 externalDeclarations.Add(sym);
			} else {
			sym = new Symbol(name, (int)TastierKind.Var, (int)type, openScopes.Count-1, getFrameAddress(currentScope), new int[] {1});
			printSymbol(sym);
			 currentScope.Add(sym);
			}
			
		}
		Expect(22);
	}

	void Stat() {
		TastierType type; string name; Symbol sym; bool external = false; bool isExternal = false;
		switch (la.kind) {
		case 1: {
			Ident(out name);
			sym = lookup(openScopes, name);
			if (sym == null) {
				sym = _lookup(externalDeclarations, name);
					isExternal = true;
			}
			
			if (sym == null) {
					SemErr("reference to undefined variable " + name);
			}
			
			if (la.kind == 5 || la.kind == 30) {
				VariableAssignment(sym);
				Expect(22);
			} else if (la.kind == 12) {
				Get();
				Expect(13);
				Expect(22);
				if ((TastierKind)sym.Item2 != TastierKind.Proc) {
				 SemErr("object is not a procedure");
				}
				
				int currentStackLevel = openScopes.Count;
				int lexicalLevelDifference = Math.Abs(openScopes.Count - sym.Item4);
				string procedureLabel = getLabelForProcedureName(lexicalLevelDifference, sym.Item1);
				program.Add(new Instruction("", "Call " + lexicalLevelDifference + " " + procedureLabel));
				
			} else SynErr(46);
			break;
		}
		case 23: {
			Get();
			Expect(12);
			Expr(out type);
			Expect(13);
			if ((TastierType)type != TastierType.Boolean) {
			 SemErr("boolean type expected");
			}
			openLabels.Push(generateLabel());
			program.Add(new Instruction("", "FJmp " + openLabels.Peek()));
			
			Stat();
			Instruction startOfElse = new Instruction(openLabels.Pop(), "Nop");
			/*
			  If we got into the "if", we need to
			  jump over the "else" so that it
			  doesn't get executed.
			*/
			openLabels.Push(generateLabel());
			program.Add(new Instruction("", "Jmp " + openLabels.Peek()));
			program.Add(startOfElse);
			
			if (la.kind == 24) {
				Get();
				Stat();
			}
			program.Add(new Instruction(openLabels.Pop(), "Nop")); 
			break;
		}
		case 25: {
			Get();
			string loopStartLabel = generateLabel();
			openLabels.Push(generateLabel()); //second label is for the loop end
			program.Add(new Instruction(loopStartLabel, "Nop"));
			
			Expect(12);
			Expr(out type);
			Expect(13);
			if ((TastierType)type != TastierType.Boolean) {
			 SemErr("boolean type expected");
			}
			program.Add(new Instruction("", "FJmp " + openLabels.Peek())); // jump to the loop end label if condition is false
			
			Stat();
			program.Add(new Instruction("", "Jmp " + loopStartLabel));
			program.Add(new Instruction(openLabels.Pop(), "Nop")); // put the loop end label here
			
			break;
		}
		case 26: {
			Get();
			if (la.kind == 1) {
				Ident(out name);
				sym = lookup(openScopes, name);
				if (sym == null) {
				 sym = _lookup(externalDeclarations, name);
				 isExternal = true;
				}
				if (sym == null) {
				 SemErr("reference to undefined variable " + name);
				}
				
				VariableAssignment(sym);
			}
			Expect(22);
			string afterUpdateActionLabel = generateLabel();
			program.Add(new Instruction("", "Jmp " + afterUpdateActionLabel));
			//this skips the update action the first time the loop is ran
			string loopStartLabel = generateLabel();
			openLabels.Push(generateLabel()); //label for end of for loop
			program.Add(new Instruction(loopStartLabel, "Nop"));
			
			Ident(out name);
			sym = lookup(openScopes, name);
			if (sym == null) {
			 sym = _lookup(externalDeclarations, name);
			 isExternal = true;
			}
			if (sym == null) {
			 SemErr("reference to undefined variable " + name);
			}
			
			VariableAssignment(sym);
			program.Add(new Instruction(afterUpdateActionLabel, "Nop"));
			
			Expect(22);
			Expr(out type);
			if ((TastierType)type != TastierType.Boolean) {
			 SemErr("boolean type expected");
			}
			program.Add(new Instruction("", "FJmp " + openLabels.Peek())); // jump to the loop end label if condition is false
			
			Expect(13);
			Expect(27);
			Stat();
			program.Add(new Instruction("", "Jmp " + loopStartLabel));
			 program.Add(new Instruction(openLabels.Pop(), "Nop")); // put the loop end label here
			
			break;
		}
		case 28: {
			Get();
			Ident(out name);
			Expect(22);
			sym = lookup(openScopes, name);
			if (sym == null) {
			 sym = _lookup(externalDeclarations, name);
			 isExternal = true;
			}
			if (sym == null) {
			 SemErr("reference to undefined variable " + name);
			}
			
			if (sym.Item2 != (int)TastierKind.Var && sym.Item2 != (int)TastierKind.Const) {
			 SemErr("variable or constant type expected but " + sym.Item1 + " has kind " + (TastierType)sym.Item2);
			}
			
			if (sym.Item3 != (int)TastierType.Integer) {
			 SemErr("integer type expected but " + sym.Item1 + " has type " + (TastierType)sym.Item2);
			}
			program.Add(new Instruction("", "Read"));
			
			if (sym.Item4 == 0) {
			 if (isExternal) {
			   program.Add(new Instruction("", "StoG " + sym.Item1));
			   // if the symbol is external, we also store it by name. The linker will resolve the name to an address.
			 } else {
			   program.Add(new Instruction("", "StoG " + (sym.Item5+3)));
			 }
			}
			else {
			 int lexicalLevelDifference = Math.Abs(openScopes.Count - sym.Item4)-1;
			 program.Add(new Instruction("", "Sto " + lexicalLevelDifference + " " + sym.Item5));
			}
			
			break;
		}
		case 29: {
			Get();
			Expr(out type);
			Expect(22);
			if (type != TastierType.Integer) {
			 SemErr("integer type expected");
			}
			program.Add(new Instruction("", "Write"));
			
			break;
		}
		case 40: {
			ArrayDecl(external);
			break;
		}
		case 14: {
			Get();
			while (StartOf(4)) {
				if (StartOf(3)) {
					Stat();
				} else {
					VarDecl(external);
				}
			}
			Expect(15);
			break;
		}
		default: SynErr(47); break;
		}
	}

	void Term(out TastierType type) {
		TastierType type1; Instruction inst; 
		Factor(out type);
		while (la.kind == 9 || la.kind == 10) {
			MulOp(out inst);
			Factor(out type1);
			if (type != TastierType.Integer ||
			   type1 != TastierType.Integer) {
			 SemErr("integer type expected");
			}
			program.Add(inst);
			
		}
	}

	void VariableAssignment(Symbol sym) {
		TastierType type; bool isExternal = false; int numberOfArrayIndexes = 0;
		program.Add(new Instruction("", "Const " + (sym.Item5)));
		Console.Write("STAT -> ADDED CONST ONTO STACK\n\n");
		if(sym.Item4 == 0 && !isExternal)
		{
		//if global, must increment address
		     program.Add(new Instruction("", "Const " + 3));
		     program.Add(new Instruction("", "Add"));
		     Console.Write("STAT -> ADDED 3 TO CONST\n\n");
		}
		
		while (la.kind == 5) {
			Get();
			Expr(out type);
			Console.Write("STAT -> ARRAY INDEXING REACHED\n\n");
			if(type != TastierType.Integer)
			{
			SemErr("non-integer index into array");
			}
			numberOfArrayIndexes++;
			if(numberOfArrayIndexes > sym.Item6.Length)
			{
			SemErr("Attempted to index into non-existent array dimension");
			}
			for(int i = 0; i < sym.Item6.Length - numberOfArrayIndexes - 1; i++)
			{
			program.Add(new Instruction("", "Const " + sym.Item6[i]));
			program.Add(new Instruction("", "Mul")); 
			}
			program.Add(new Instruction("", "Add"));
			
			Expect(6);
		}
		Expect(30);
		if ((TastierKind)sym.Item2 != TastierKind.Var && (TastierKind)sym.Item2 != TastierKind.Array) {
		 SemErr("cannot assign to non-variable");
		}
		
		Expr(out type);
		if (la.kind == 31) {
			Get();
			Console.Write("STAT -> CONDITIONAL ASSIGNMENT REACHED");
			if ((TastierType)type != TastierType.Boolean) {
			SemErr("boolean type expected");
			}
			openLabels.Push(generateLabel());
			program.Add(new Instruction("", "FJmp " + openLabels.Peek()));
			
			Expr(out type);
			if (type != (TastierType)sym.Item3) {
			 SemErr("incompatible types");
			}
			
			Instruction startOfElse = new Instruction(openLabels.Pop(), "Nop");
			/*
			  If we got into the "if", we need to
			  jump over the "else" so that it
			  doesn't get executed.
			*/
			openLabels.Push(generateLabel());
			program.Add(new Instruction("", "Jmp " + openLabels.Peek()));
			program.Add(startOfElse);
			
			Expect(32);
			Expr(out type);
			if (type != (TastierType)sym.Item3) {
			 SemErr("incompatible types");
			}
			program.Add(new Instruction(openLabels.Pop(), "Nop"));
			
		}
		type = (TastierType)sym.Item3;
		if ((TastierKind)sym.Item2 == TastierKind.Var || (TastierKind)sym.Item2 == TastierKind.Const || (TastierKind)sym.Item2 == TastierKind.Array) {
		if (sym.Item4 == 0) {
		
			if (isExternal) {
			  program.Add(new Instruction("", "StoIG"));
			  // if the symbol is external, we load it by name. The linker will resolve the name to an address.
			} else {
		
		Console.Write("STAT -> STOIG NOT EXTERNAL REACHED\n\n");
			  program.Add(new Instruction("", "StoIG"));
			}
		} else {
			int lexicalLevelDifference = Math.Abs(openScopes.Count - sym.Item4)-1;
			program.Add(new Instruction("", "StoI " + lexicalLevelDifference));
		}
		} else SemErr("variable, constant or array identifier expected");
		
	}

	void ArrayDecl(bool external) {
		string name; TastierType type; Scope currentScope = openScopes.Peek(); ArrayList dimensions = new ArrayList(); int n;
		
		Expect(40);
		Type(out type);
		Ident(out name);
		Expect(5);
		Expect(2);
		n = Convert.ToInt32(t.val);
		dimensions.Insert(0, n);
		
		Expect(6);
		while (la.kind == 5) {
			Get();
			Expect(2);
			n = Convert.ToInt32(t.val);
			dimensions.Insert(0, n);
			
			Expect(6);
		}
		Symbol sym; 
		if (external) {
		sym = new Symbol(name, (int)TastierKind.Array, (int)type, 0, 0, new int[] {1});
		externalDeclarations.Add(sym);
		printSymbol(sym);
		} else {
		sym = new Symbol(name, (int)TastierKind.Array, (int)type, openScopes.Count-1, getFrameAddress(currentScope), dimensions.Cast<int>().ToArray());
		currentScope.Add(sym);
		printSymbol(sym);
		}
		
		Expect(22);
	}

	void Tastier() {
		string name; bool external = false; 
		Expect(33);
		Ident(out name);
		openScopes.Push(new Scope());
		
		Expect(14);
		ConstProc();
		while (StartOf(5)) {
			if (la.kind == 34 || la.kind == 35) {
				VarDecl(external);
			} else if (la.kind == 11) {
				ProcDecl();
			} else {
				ExternDecl();
			}
		}
		Expect(15);
		if (openScopes.Peek().Count == 0) {
		 Warn("Warning: Program " + name + " is empty ");
		}
		
		header.Add(new Instruction("", ".names " + (externalDeclarations.Count + openScopes.Peek().Count)));
		foreach (Symbol s in openScopes.Peek()) {
		 if (s.Item2 == (int)TastierKind.Var) {
		   header.Add(new Instruction("", ".var " + ((int)s.Item3) + " " + s.Item1));
		 } else if (s.Item2 == (int)TastierKind.Proc) {
		   header.Add(new Instruction("", ".proc " + s.Item1));
		 } else if (s.Item2 == (int)TastierKind.Const) {
		header.Add(new Instruction("", ".Var " + s.Item1));
		} else {
		   SemErr("global item " + s.Item1 + " has no defined type");
		 }
		}
		foreach (Symbol s in externalDeclarations) {
		 if (s.Item2 == (int)TastierKind.Var) {
		   header.Add(new Instruction("", ".external var " + ((int)s.Item3) + " " + s.Item1));
		 } else if (s.Item2 == (int)TastierKind.Proc) {
		   header.Add(new Instruction("", ".external proc " + s.Item1));
		 } else {
		   SemErr("external item " + s.Item1 + " has no defined type");
		 }
		}
		header.AddRange(program);
		openScopes.Pop();
		
	}

	void ConstProc() {
		string name; string label; Scope currentScope = openScopes.Peek(); int enterInstLocation = 0; 
		Expect(39);
		name = "Const";
		                           Symbol tempSymbolPointer = new Symbol(name, (int)TastierKind.Proc, (int)TastierType.Undefined, openScopes.Count, -1, new int[] {1}); 
		                           currentScope.Add(tempSymbolPointer);
		printSymbol(tempSymbolPointer);
		                       
		Expect(14);
		program.Add(new Instruction("", "Enter 0"));
		enterInstLocation = program.Count - 1;
		label = generateProcedureName(name);
		openProcedureDeclarations.Push(name);
		/*
		 Enter is supposed to have as an
		 argument the next free address on the
		 stack, but until we know how many
		 local variables are in this procedure,
		 we don't know what that is. We'll keep
		 track of where we put the Enter
		 instruction in the program so that
		 later, when we know how many spaces on
		 the stack have been allocated, we can
		 put the right value in.
		*/
		
		while (la.kind == 34 || la.kind == 35) {
			ConstDecl();
		}
		
		Expect(15);
		program.Add(new Instruction("", "Leave"));
		program.Add(new Instruction("", "Ret"));
		// now we can generate the Enter instruction properly
		program[enterInstLocation] =
		 new Instruction(label, "Enter " +
		                 getFrameAddress(currentScope));
		openProcedureDeclarations.Pop();
		
	}

	void ExternDecl() {
		string name; bool external = true; 
		Expect(37);
		if (la.kind == 34 || la.kind == 35) {
			VarDecl(external);
		} else if (la.kind == 38) {
			Get();
			Ident(out name);
			Expect(22);
			Symbol sym = new Symbol(name, (int)TastierKind.Proc, (int)TastierType.Undefined, 1, -1, new int[] {1});
			externalDeclarations.Add(sym); 
			printSymbol(sym);
			
		} else SynErr(48);
	}

	void Type(out TastierType type) {
		type = TastierType.Undefined; 
		if (la.kind == 34) {
			Get();
			type = TastierType.Integer; 
		} else if (la.kind == 35) {
			Get();
			type = TastierType.Boolean; 
		} else SynErr(49);
	}

	void ConstDecl() {
		string name; TastierType type; Scope currentScope = openScopes.Peek();
		
		Type(out type);
		Ident(out name);
		Symbol tempSymbolPointer;
		tempSymbolPointer = new Symbol(name, (int)TastierKind.Const, (int)type, openScopes.Count-1, getFrameAddress(currentScope), new int[] {1});
		                         currentScope.Add(tempSymbolPointer);
		printSymbol(tempSymbolPointer);
		                     
		Expect(30);
		Expr(out type);
		if (type != (TastierType)tempSymbolPointer.Item3) {
		 SemErr("incompatible types");
		}
		if (tempSymbolPointer.Item4 == 0) {
		   program.Add(new Instruction("", "StoG " + (tempSymbolPointer.Item5+3)));
		}
		else {
		 int lexicalLevelDifference = Math.Abs(openScopes.Count - tempSymbolPointer.Item4)-1;
		 program.Add(new Instruction("", "Sto " + lexicalLevelDifference + " " + tempSymbolPointer.Item5));
		}
		
		Expect(22);
	}



	public void Parse() {
		la = new Token();
		la.val = "";
		Get();
		Tastier();
		Expect(0);

	}

	static readonly bool[,] set = {
		{T,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x},
		{x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, T,T,T,T, T,T,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x},
		{x,T,x,x, x,x,x,x, x,x,x,T, x,x,T,x, x,x,x,x, x,x,x,T, x,T,T,x, T,T,x,x, x,x,T,T, x,x,x,x, T,x,x},
		{x,T,x,x, x,x,x,x, x,x,x,x, x,x,T,x, x,x,x,x, x,x,x,T, x,T,T,x, T,T,x,x, x,x,x,x, x,x,x,x, T,x,x},
		{x,T,x,x, x,x,x,x, x,x,x,x, x,x,T,x, x,x,x,x, x,x,x,T, x,T,T,x, T,T,x,x, x,x,T,T, x,x,x,x, T,x,x},
		{x,x,x,x, x,x,x,x, x,x,x,T, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,T,T, x,T,x,x, x,x,x}

	};
} // end Parser


public class Errors {
	public int count = 0;                                    // number of errors detected
	public System.IO.TextWriter errorStream = Console.Out;   // error messages go to this stream
	public string errMsgFormat = "-- line {0} col {1}: {2}"; // 0=line, 1=column, 2=text

	public virtual void SynErr (int line, int col, int n) {
		string s;
		switch (n) {
			case 0: s = "EOF expected"; break;
			case 1: s = "ident expected"; break;
			case 2: s = "number expected"; break;
			case 3: s = "\"+\" expected"; break;
			case 4: s = "\"-\" expected"; break;
			case 5: s = "\"[\" expected"; break;
			case 6: s = "\"]\" expected"; break;
			case 7: s = "\"true\" expected"; break;
			case 8: s = "\"false\" expected"; break;
			case 9: s = "\"*\" expected"; break;
			case 10: s = "\"/\" expected"; break;
			case 11: s = "\"void\" expected"; break;
			case 12: s = "\"(\" expected"; break;
			case 13: s = "\")\" expected"; break;
			case 14: s = "\"{\" expected"; break;
			case 15: s = "\"}\" expected"; break;
			case 16: s = "\"=\" expected"; break;
			case 17: s = "\"<\" expected"; break;
			case 18: s = "\">\" expected"; break;
			case 19: s = "\"<=\" expected"; break;
			case 20: s = "\">=\" expected"; break;
			case 21: s = "\"!=\" expected"; break;
			case 22: s = "\";\" expected"; break;
			case 23: s = "\"if\" expected"; break;
			case 24: s = "\"else\" expected"; break;
			case 25: s = "\"while\" expected"; break;
			case 26: s = "\"for(\" expected"; break;
			case 27: s = "\"do\" expected"; break;
			case 28: s = "\"read\" expected"; break;
			case 29: s = "\"write\" expected"; break;
			case 30: s = "\":=\" expected"; break;
			case 31: s = "\"?\" expected"; break;
			case 32: s = "\":\" expected"; break;
			case 33: s = "\"program\" expected"; break;
			case 34: s = "\"int\" expected"; break;
			case 35: s = "\"bool\" expected"; break;
			case 36: s = "\",\" expected"; break;
			case 37: s = "\"external\" expected"; break;
			case 38: s = "\"procedure\" expected"; break;
			case 39: s = "\"Const\" expected"; break;
			case 40: s = "\"array\" expected"; break;
			case 41: s = "??? expected"; break;
			case 42: s = "invalid AddOp"; break;
			case 43: s = "invalid RelOp"; break;
			case 44: s = "invalid Factor"; break;
			case 45: s = "invalid MulOp"; break;
			case 46: s = "invalid Stat"; break;
			case 47: s = "invalid Stat"; break;
			case 48: s = "invalid ExternDecl"; break;
			case 49: s = "invalid Type"; break;

			default: s = "error " + n; break;
		}
		errorStream.WriteLine(errMsgFormat, line, col, s);
		count++;
	}

	public virtual void SemErr (int line, int col, string s) {
		errorStream.WriteLine(errMsgFormat, line, col, s);
		count++;
	}

	public virtual void SemErr (string s) {
		errorStream.WriteLine(s);
		count++;
	}

	public virtual void Warning (int line, int col, string s) {
		errorStream.WriteLine(errMsgFormat, line, col, s);
	}

	public virtual void Warning(string s) {
		errorStream.WriteLine(s);
	}
} // Errors


public class FatalError: Exception {
	public FatalError(string m): base(m) {}
}
}