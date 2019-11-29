namespace Atom2
{
  using System;
  using System.Collections.Generic;

  public delegate void OutputtingEventHandler(object sender, string message);

  public delegate void TerminatingEventHandler(object sender, Exception exception);

  public sealed class CharHashSet : HashSet<char>
  {
  }

  public sealed class StringHashSet : HashSet<string>
  {
  }

  public sealed class NameHashSet : HashSet<Name>
  {
  }

  public sealed class Stack : Stack<object>
  {
  }

  public sealed class Tokens : Queue<object>
  {
  }

  public sealed class Words : ScopedDictionary<Name, object>
  {
  }

  public sealed class CallEnvironments : Stack<CallEnvironment>
  {
  }
}