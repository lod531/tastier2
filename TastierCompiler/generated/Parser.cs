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
	public const int _letter = 3;
	public const int _string = 4;
	public const int _dereference = 5;
	public const int maxT = 49;

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
    Boolean,
    Character
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
		case ((int)TastierKind.Array):
			kindString = "array";
			break;
		default:
			kindString = "oh man I have no idea";
			break;
	}
	Console.Write(kindString + " ");
	if((int)sym.Item2 == (int)TastierKind.Var 
		|| (int)sym.Item2 == (int)TastierKind.Const
		|| (int)sym.Item2 == (int)TastierKind.Array)
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
	if((int)sym.Item2 == (int)TastierKind.Array)
	{
		Console.Write("with dimensions ");
		for(int i = sym.Item6.Length - 1; i >= 0; i--)
		{
			Console.Write("[" + sym.Item6[i] + "]");
		}
		Console.Write(" ");
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
		if (la.kind == 6) {
			Get();
		} else if (la.kind == 7) {
			Get();
			inst = new Instruction("", "Sub"); 
		} else SynErr(50);
	}

	void SymbolIdent(out string name) {
		string tempName; 
		Ident(out name);
		while (la.kind == 5) {
			Get();
			Ident(out tempName);
			name = name + "->" + tempName;	
			
		}
	}

	void Ident(out string name) {
		Expect(1);
		name = t.val; 
	}

	void SymbolLookup(out Symbol sym, out bool isExternal) {
		string name; 
		SymbolIdent(out name);
		isExternal = false; //CS3071 students can ignore external declarations, since they only deal with compilation of single files.
		sym = lookup(openScopes, name);
		if (sym == null) {
		sym = _lookup(externalDeclarations, name);
		isExternal = true;
		}
		
		if (sym == null) {
		SemErr("reference to undefined variable " + name);
		}
		
	}

	void Expr(out TastierType type) {
		TastierType type1; TastierType type2; Instruction inst; 
		type = TastierType.Undefined;
		//this is just to get this to compile, the type will either be changed
		//or the program will crash
		
		if (StartOf(1)) {
			SimExpr(out type);
			if (StartOf(2)) {
				RelOp(out inst);
				SimExpr(out type1);
				if (type != type1) {
				 SemErr("incompatible types");
				}
				if(type == TastierType.Character)
				{
				SemErr("don't try to do boolean ops with characters <_<");
				}
				else {
				 program.Add(inst);
				 type = TastierType.Boolean;
				}
				
			}
		} else if (la.kind == 8) {
			Get();
			Expr(out type);
			Expect(9);
			if (la.kind == 10) {
				Get();
				if ((TastierType)type != TastierType.Boolean) {
				 SemErr("boolean type expected");
				}
				openLabels.Push(generateLabel());
				program.Add(new Instruction("", "FJmp " + openLabels.Peek()));
				
				Expr(out type1);
				Instruction startOfElse = new Instruction(openLabels.Pop(), "Nop");
				openLabels.Push(generateLabel());
				program.Add(new Instruction("", "Jmp " + openLabels.Peek()));
				program.Add(startOfElse);
				
				Expect(11);
				Expr(out type2);
				if (type1 != type2) {
				 SemErr("(boolean)? type1 : type2, but types 1 and 2 are mismatched!");
				}
				program.Add(new Instruction(openLabels.Pop(), "Nop"));
				type = type1;
				
			}
		} else SynErr(51);
	}

	void SimExpr(out TastierType type) {
		TastierType type1; Instruction inst; 
		Term(out type);
		while (la.kind == 6 || la.kind == 7) {
			AddOp(out inst);
			Term(out type1);
			if (type != TastierType.Integer || type1 != TastierType.Integer) {
			 SemErr("integer type expected");
			}
			if(type == TastierType.Character)
			{
			SemErr("character arithmetic not support I'm afraid");
			}
			program.Add(inst);
			
		}
	}

	void RelOp(out Instruction inst) {
		inst = new Instruction("", "Equ"); 
		switch (la.kind) {
		case 19: {
			Get();
			break;
		}
		case 20: {
			Get();
			inst = new Instruction("", "Lss"); 
			break;
		}
		case 21: {
			Get();
			inst = new Instruction("", "Gtr"); 
			break;
		}
		case 22: {
			Get();
			inst = new Instruction("", "Leq"); 
			break;
		}
		case 23: {
			Get();
			inst = new Instruction("", "Geq"); 
			break;
		}
		case 24: {
			Get();
			inst = new Instruction("", "Neq"); 
			break;
		}
		default: SynErr(52); break;
		}
	}

	void Factor(out TastierType type) {
		int n; Symbol sym; int letterNumerical; bool isExternal; 
		type = TastierType.Undefined; 
		switch (la.kind) {
		case 1: {
			SymbolLookup(out sym, out isExternal);
			VariableAddress(sym, isExternal);
			type = (TastierType)sym.Item3;
			if ((TastierKind)sym.Item2 == TastierKind.Var || (TastierKind)sym.Item2 == TastierKind.Const || (TastierKind)sym.Item2 == TastierKind.Array) {
			 if (sym.Item4 == 0) {
			
			     if (isExternal) {
			       program.Add(new Instruction("", "LoadIG"));
			       // if the symbol is external, we load it by name. The linker will resolve the name to an address.
			     } else {
			       program.Add(new Instruction("", "LoadIG"));
			     }
			 } else {
			     int lexicalLevelDifference = Math.Abs(openScopes.Count - sym.Item4)-1;
			     program.Add(new Instruction("", "LoadI " + lexicalLevelDifference));
			 }
			} else SemErr("variable, constant or array identifier expected");
			
			break;
		}
		case 2: {
			Get();
			n = Convert.ToInt32(t.val);
			program.Add(new Instruction("", "Const " + n));
			type = TastierType.Integer;
			
			break;
		}
		case 3: {
			Get();
			letterNumerical = Convert.ToInt32(t.val.ToCharArray()[1]);
			program.Add(new Instruction("", "Const " + letterNumerical));
			type = TastierType.Character;
			
			break;
		}
		case 7: {
			Get();
			Factor(out type);
			if (type != TastierType.Integer) {
			 SemErr("integer type expected");
			 type = TastierType.Integer;
			}
			program.Add(new Instruction("", "Neg"));
			program.Add(new Instruction("", "Const 1"));
			program.Add(new Instruction("", "Add"));
			
			break;
		}
		case 12: {
			Get();
			program.Add(new Instruction("", "Const " + 1)); type = TastierType.Boolean; 
			break;
		}
		case 13: {
			Get();
			program.Add(new Instruction("", "Const " + 0)); type = TastierType.Boolean; 
			break;
		}
		default: SynErr(53); break;
		}
	}

	void VariableAddress(Symbol sym, bool isExternal) {
		TastierType type; int numberOfArrayIndexes = 0; 
		program.Add(new Instruction("", "Const " + (sym.Item5)));
		if(sym.Item4 == 0 && !isExternal)
		{
		//if global, must increment address
		     program.Add(new Instruction("", "Const " + 3));
		     program.Add(new Instruction("", "Add"));
		}
		
		while (la.kind == 26) {
			Get();
			Expr(out type);
			if(sym.Item2 != (int)TastierKind.Array)
			{
			SemErr("attempted to index into a non-array variable");
			}
			if(type != TastierType.Integer)
			{
			SemErr("non-integer index into array");
			}
			numberOfArrayIndexes++;
			
			
			//RUNTIME ARRAY BOUNDS CHECKING STARTS HERE
			//duping expr for comparison purposes
			program.Add(new Instruction("", "Dup"));
			//pushing on the relevant array bound
			program.Add(new Instruction("", "Const " + sym.Item6[sym.Item6.Length - numberOfArrayIndexes]));
			program.Add(new Instruction("", "Gtr"));
			openLabels.Push(generateLabel());
			program.Add(new Instruction("", "FJmp " + openLabels.Peek()));
			//if(out of Bounds) then error else skip over ArrayOutOfBounds
			program.Add(new Instruction("", "ArrayOutOfBounds"));
			//RUNTIME ARRAY BOUNDS CHECKING ENDS HERE
			
			
			program.Add(new Instruction(openLabels.Pop(), "Nop"));
			if(numberOfArrayIndexes > sym.Item6.Length)
			{
			SemErr("Attempted to index into non-existent array dimension");
			}
			for(int i = 0; i < sym.Item6.Length - numberOfArrayIndexes; i++)
			{
			program.Add(new Instruction("", "Const " + sym.Item6[i]));
			program.Add(new Instruction("", "Mul")); 
			}
			program.Add(new Instruction("", "Add"));
			
			Expect(27);
		}
	}

	void MulOp(out Instruction inst) {
		inst = new Instruction("", "Mul"); 
		if (la.kind == 14) {
			Get();
		} else if (la.kind == 15) {
			Get();
			inst = new Instruction("", "Div"); 
		} else SynErr(54);
	}

	void ProcDecl() {
		string name; string label; Scope currentScope = openScopes.Peek(); int enterInstLocation = 0; bool external = false; 
		Expect(16);
		Ident(out name);
		Symbol sym = new Symbol(name, (int)TastierKind.Proc, (int)TastierType.Undefined, openScopes.Count, -1, new int[] {1});
		                         currentScope.Add(sym);
		printSymbol(sym);
		                         openScopes.Push(new Scope());
		                         currentScope = openScopes.Peek();
		                     
		Expect(8);
		Expect(9);
		Expect(17);
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
		
		while (StartOf(3)) {
			if (la.kind == 41 || la.kind == 42) {
				VarDecl(external, "");
			} else if (StartOf(4)) {
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
		Expect(18);
		program.Add(new Instruction("", "Leave"));
		program.Add(new Instruction("", "Ret"));
		openScopes.Pop();
		// now we can generate the Enter instruction properly
		program[enterInstLocation] =
		 new Instruction(label, "Enter " + getFrameAddress(currentScope));
		                                   openProcedureDeclarations.Pop();
		
	}

	void VarDecl(bool external, string symbolNamePrefix) {
		string name; TastierType type; Scope currentScope = openScopes.Peek();
		
		Type(out type);
		Ident(out name);
		Symbol sym; 
		name = symbolNamePrefix + name;
		if (external) {
		sym = new Symbol(name, (int)TastierKind.Var, (int)type, 0, 0, new int[] {1});
		externalDeclarations.Add(sym);
		printSymbol(sym);
		} else {
		sym = new Symbol(name, (int)TastierKind.Var, (int)type, openScopes.Count-1, getFrameAddress(currentScope), new int[] {1});
		currentScope.Add(sym);
		printSymbol(sym);
		}
		                          
		while (la.kind == 43) {
			Get();
			Ident(out name);
			name = symbolNamePrefix + name;
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
		Expect(25);
	}

	void Stat() {
		Symbol sym; bool external = false; bool isExternal;
		switch (la.kind) {
		case 1: {
			SymbolLookup(out sym, out isExternal);
			if (la.kind == 26 || la.kind == 28) {
				VariableAssignment(sym, isExternal);
				Expect(25);
			} else if (la.kind == 8) {
				Get();
				Expect(9);
				Expect(25);
				if ((TastierKind)sym.Item2 != TastierKind.Proc) {
				 SemErr("object is not a procedure");
				}
				int currentStackLevel = openScopes.Count;
				int lexicalLevelDifference = Math.Abs(openScopes.Count - sym.Item4);
				string procedureLabel = getLabelForProcedureName(lexicalLevelDifference, sym.Item1);
				program.Add(new Instruction("", "Call " + lexicalLevelDifference + " " + procedureLabel));
				
			} else SynErr(55);
			break;
		}
		case 29: {
			IfStat();
			break;
		}
		case 36: {
			SwitchStat();
			break;
		}
		case 31: {
			WhileStat();
			break;
		}
		case 32: {
			ForStat();
			break;
		}
		case 34: {
			ReadStat();
			break;
		}
		case 35: {
			WriteStat();
			break;
		}
		case 47: {
			ArrayDecl(external, "");
			break;
		}
		case 48: {
			StructDecl(external, "");
			break;
		}
		case 17: {
			Get();
			while (StartOf(5)) {
				if (StartOf(4)) {
					Stat();
				} else {
					VarDecl(external, "");
				}
			}
			Expect(18);
			break;
		}
		default: SynErr(56); break;
		}
	}

	void Term(out TastierType type) {
		TastierType type1; Instruction inst; 
		Factor(out type);
		while (la.kind == 14 || la.kind == 15) {
			MulOp(out inst);
			Factor(out type1);
			if (type != TastierType.Integer ||
			   type1 != TastierType.Integer) {
			 SemErr("integer type expected");
			}
			else if(type == TastierType.Character)
			{
			SemErr("character arithmetic not supported I'm afradi");
			}
			program.Add(inst);
			
		}
	}

	void VariableAssignment(Symbol sym, bool isExternal) {
		TastierType type; 
		VariableAddress(sym, isExternal);
		Expect(28);
		if ((TastierKind)sym.Item2 != TastierKind.Var && (TastierKind)sym.Item2 != TastierKind.Array) {
		SemErr("cannot assign to non-variable");
		}
		
		Expr(out type);
		type = (TastierType)sym.Item3;
		if ((TastierKind)sym.Item2 == TastierKind.Var || (TastierKind)sym.Item2 == TastierKind.Const || (TastierKind)sym.Item2 == TastierKind.Array) {
		if (sym.Item4 == 0) {
		
			if (isExternal) {
			  program.Add(new Instruction("", "StoIG"));
			  // if the symbol is external, we load it by name. The linker will resolve the name to an address.
			} else {
			  program.Add(new Instruction("", "StoIG"));
			}
		} else {
			int lexicalLevelDifference = Math.Abs(openScopes.Count - sym.Item4)-1;
			program.Add(new Instruction("", "StoI " + lexicalLevelDifference));
		}
		} else SemErr("variable, constant or array identifier expected");
		
	}

	void IfStat() {
		TastierType type; 
		Expect(29);
		Expect(8);
		Expr(out type);
		Expect(9);
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
		
		if (la.kind == 30) {
			Get();
			Stat();
		}
		program.Add(new Instruction(openLabels.Pop(), "Nop")); 
	}

	void SwitchStat() {
		TastierType type; 
		Expect(36);
		Expr(out type);
		if (type != TastierType.Integer)
		{
		SemErr("integer type expected!");
		}
		string endOfSwitchStatement = generateLabel();	//the label pushed on here
							//signifies the end of the
							//switch statement
		
		Expect(9);
		Expect(17);
		while (la.kind == 37) {
			Get();
			program.Add(new Instruction("", "Dup"));
			
			Expr(out type);
			if(type != TastierType.Integer)
			{
			SemErr("integer type expected!");
			}
			program.Add(new Instruction("", "Equ"));
			openLabels.Push(generateLabel());
			program.Add(new Instruction("", "FJmp " + openLabels.Peek()));
			
			Expect(11);
			Stat();
			program.Add(new Instruction("", "Jmp " + endOfSwitchStatement));
			
			Expect(38);
			program.Add(new Instruction(openLabels.Pop(), "Nop"));
			
		}
		Expect(39);
		Stat();
		program.Add(new Instruction(endOfSwitchStatement, "Nop"));
		program.Add(new Instruction("", "Pop"));
		
		Expect(18);
	}

	void WhileStat() {
		TastierType type; 
		Expect(31);
		string loopStartLabel = generateLabel();
		openLabels.Push(generateLabel()); //second label is for the loop end
		program.Add(new Instruction(loopStartLabel, "Nop"));
		
		Expect(8);
		Expr(out type);
		Expect(9);
		if ((TastierType)type != TastierType.Boolean) {
		 SemErr("boolean type expected");
		}
		program.Add(new Instruction("", "FJmp " + openLabels.Peek())); // jump to the loop end label if condition is false
		
		Stat();
		program.Add(new Instruction("", "Jmp " + loopStartLabel));
		program.Add(new Instruction(openLabels.Pop(), "Nop")); // put the loop end label here
		
	}

	void ForStat() {
		TastierType type; string name; Symbol sym; bool isExternal = false;
		Expect(32);
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
			
			VariableAssignment(sym, isExternal);
		}
		Expect(25);
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
		
		VariableAssignment(sym, isExternal);
		program.Add(new Instruction(afterUpdateActionLabel, "Nop"));
		
		Expect(25);
		Expr(out type);
		if ((TastierType)type != TastierType.Boolean) {
		 SemErr("boolean type expected");
		}
		program.Add(new Instruction("", "FJmp " + openLabels.Peek())); // jump to the loop end label if condition is false
		
		Expect(9);
		Expect(33);
		Stat();
		program.Add(new Instruction("", "Jmp " + loopStartLabel));
		program.Add(new Instruction(openLabels.Pop(), "Nop")); // put the loop end label here
		
	}

	void ReadStat() {
		string name; Symbol sym; bool isExternal = false;
		Expect(34);
		Ident(out name);
		Expect(25);
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
		
	}

	void WriteStat() {
		TastierType type; char[] charArray; 
		Expect(35);
		if (StartOf(6)) {
			Expr(out type);
			if (type != TastierType.Integer && type != TastierType.Character) {
			SemErr("integer type expected");
			}
			if(type == TastierType.Integer)
			{
			program.Add(new Instruction("", "Write"));
			}
			else if(type == TastierType.Character)
			{
			program.Add(new Instruction("", "CharWrite"));
			}
			
		} else if (la.kind == 4) {
			Get();
			charArray = t.val.ToCharArray();
			//altered for bounds because first and last chars are not applicable (they're dashes)
			for(int i = 1; i < charArray.Length-1; i++)
			{
			program.Add(new Instruction("", "Const " + Convert.ToInt32(charArray[i])));
			program.Add(new Instruction("", "CharWrite"));
			}	
			
		} else SynErr(57);
		Expect(25);
	}

	void ArrayDecl(bool external, string symbolNamePrefix) {
		string name; TastierType type; Scope currentScope = openScopes.Peek(); ArrayList dimensions = new ArrayList(); int n;
		
		Expect(47);
		Type(out type);
		Ident(out name);
		Expect(26);
		Expect(2);
		n = Convert.ToInt32(t.val);
		dimensions.Insert(0, n);
		
		Expect(27);
		while (la.kind == 26) {
			Get();
			Expect(2);
			n = Convert.ToInt32(t.val);
			dimensions.Insert(0, n);
			
			Expect(27);
		}
		Symbol sym; 
		name = symbolNamePrefix + name;
		if (external) {
		sym = new Symbol(name, (int)TastierKind.Array, (int)type, 0, 0, new int[] {1});
		externalDeclarations.Add(sym);
		printSymbol(sym);
		} else {
		sym = new Symbol(name, (int)TastierKind.Array, (int)type, openScopes.Count-1, getFrameAddress(currentScope), dimensions.Cast<int>().ToArray());
		currentScope.Add(sym);
		printSymbol(sym);
		}
		
		if (la.kind == 43) {
			Get();
			ArrayDecl(false, symbolNamePrefix);
		} else if (la.kind == 25) {
			Get();
		} else SynErr(58);
	}

	void StructDecl(bool external, string symbolNamePrefix) {
		string name; 
		
		Expect(48);
		Ident(out name);
		Expect(17);
		while (la.kind == 41 || la.kind == 42 || la.kind == 47) {
			if (la.kind == 47) {
				ArrayDecl(external, name + "->");
			} else {
				VarDecl(external, name + "->");
			}
		}
		Expect(18);
		Expect(25);
	}

	void Tastier() {
		string name; bool external = false; 
		Expect(40);
		Ident(out name);
		openScopes.Push(new Scope());
		
		Expect(17);
		ConstProc();
		while (StartOf(7)) {
			if (la.kind == 41 || la.kind == 42) {
				VarDecl(external, "");
			} else if (la.kind == 16) {
				ProcDecl();
			} else {
				ExternDecl();
			}
		}
		Expect(18);
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
		Expect(46);
		name = "Const";
		                           Symbol tempSymbolPointer = new Symbol(name, (int)TastierKind.Proc, (int)TastierType.Undefined, openScopes.Count, -1, new int[] {1}); 
		                           currentScope.Add(tempSymbolPointer);
		printSymbol(tempSymbolPointer);
		                       
		Expect(17);
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
		
		while (la.kind == 41 || la.kind == 42) {
			ConstDecl();
		}
		
		Expect(18);
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
		Expect(44);
		if (la.kind == 41 || la.kind == 42) {
			VarDecl(external, "");
		} else if (la.kind == 45) {
			Get();
			Ident(out name);
			Expect(25);
			Symbol sym = new Symbol(name, (int)TastierKind.Proc, (int)TastierType.Undefined, 1, -1, new int[] {1});
			externalDeclarations.Add(sym); 
			printSymbol(sym);
			
		} else SynErr(59);
	}

	void Type(out TastierType type) {
		type = TastierType.Undefined; 
		if (la.kind == 41) {
			Get();
			type = TastierType.Integer; 
		} else if (la.kind == 42) {
			Get();
			type = TastierType.Boolean; 
		} else SynErr(60);
	}

	void ConstDecl() {
		string name; TastierType type; Scope currentScope = openScopes.Peek();
		
		Type(out type);
		Ident(out name);
		Symbol tempSymbolPointer;
		tempSymbolPointer = new Symbol(name, (int)TastierKind.Const, (int)type, openScopes.Count-1, getFrameAddress(currentScope), new int[] {1});
		                         currentScope.Add(tempSymbolPointer);
		printSymbol(tempSymbolPointer);
		                     
		Expect(28);
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
		
		Expect(25);
	}



	public void Parse() {
		la = new Token();
		la.val = "";
		Get();
		Tastier();
		Expect(0);

	}

	static readonly bool[,] set = {
		{T,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x},
		{x,T,T,T, x,x,x,T, x,x,x,x, T,T,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x},
		{x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,T, T,T,T,T, T,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x},
		{x,T,x,x, x,x,x,x, x,x,x,x, x,x,x,x, T,T,x,x, x,x,x,x, x,x,x,x, x,T,x,T, T,x,T,T, T,x,x,x, x,T,T,x, x,x,x,T, T,x,x},
		{x,T,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,T,x,x, x,x,x,x, x,x,x,x, x,T,x,T, T,x,T,T, T,x,x,x, x,x,x,x, x,x,x,T, T,x,x},
		{x,T,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,T,x,x, x,x,x,x, x,x,x,x, x,T,x,T, T,x,T,T, T,x,x,x, x,T,T,x, x,x,x,T, T,x,x},
		{x,T,T,T, x,x,x,T, T,x,x,x, T,T,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x},
		{x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, T,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,T,T,x, T,x,x,x, x,x,x}

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
			case 3: s = "letter expected"; break;
			case 4: s = "string expected"; break;
			case 5: s = "dereference expected"; break;
			case 6: s = "\"+\" expected"; break;
			case 7: s = "\"-\" expected"; break;
			case 8: s = "\"(\" expected"; break;
			case 9: s = "\")\" expected"; break;
			case 10: s = "\"?\" expected"; break;
			case 11: s = "\":\" expected"; break;
			case 12: s = "\"true\" expected"; break;
			case 13: s = "\"false\" expected"; break;
			case 14: s = "\"*\" expected"; break;
			case 15: s = "\"/\" expected"; break;
			case 16: s = "\"void\" expected"; break;
			case 17: s = "\"{\" expected"; break;
			case 18: s = "\"}\" expected"; break;
			case 19: s = "\"=\" expected"; break;
			case 20: s = "\"<\" expected"; break;
			case 21: s = "\">\" expected"; break;
			case 22: s = "\"<=\" expected"; break;
			case 23: s = "\">=\" expected"; break;
			case 24: s = "\"!=\" expected"; break;
			case 25: s = "\";\" expected"; break;
			case 26: s = "\"[\" expected"; break;
			case 27: s = "\"]\" expected"; break;
			case 28: s = "\":=\" expected"; break;
			case 29: s = "\"if\" expected"; break;
			case 30: s = "\"else\" expected"; break;
			case 31: s = "\"while\" expected"; break;
			case 32: s = "\"for(\" expected"; break;
			case 33: s = "\"do\" expected"; break;
			case 34: s = "\"read\" expected"; break;
			case 35: s = "\"write\" expected"; break;
			case 36: s = "\"switch(\" expected"; break;
			case 37: s = "\"case\" expected"; break;
			case 38: s = "\"break;\" expected"; break;
			case 39: s = "\"default:\" expected"; break;
			case 40: s = "\"program\" expected"; break;
			case 41: s = "\"int\" expected"; break;
			case 42: s = "\"bool\" expected"; break;
			case 43: s = "\",\" expected"; break;
			case 44: s = "\"external\" expected"; break;
			case 45: s = "\"procedure\" expected"; break;
			case 46: s = "\"Const\" expected"; break;
			case 47: s = "\"array\" expected"; break;
			case 48: s = "\"struct\" expected"; break;
			case 49: s = "??? expected"; break;
			case 50: s = "invalid AddOp"; break;
			case 51: s = "invalid Expr"; break;
			case 52: s = "invalid RelOp"; break;
			case 53: s = "invalid Factor"; break;
			case 54: s = "invalid MulOp"; break;
			case 55: s = "invalid Stat"; break;
			case 56: s = "invalid Stat"; break;
			case 57: s = "invalid WriteStat"; break;
			case 58: s = "invalid ArrayDecl"; break;
			case 59: s = "invalid ExternDecl"; break;
			case 60: s = "invalid Type"; break;

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