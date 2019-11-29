#pragma warning disable 618

namespace Atom2
{
  using System.Collections.Generic;

  public class ScopedDictionary<TK, TV>
  {
    private readonly Scopes scopes = new Scopes();

    protected ScopedDictionary()
    {
      EnterScope();
    }

    public TV this[TK key]
    {
      get => TryGetValue(key, out TV result) ? result : default;
      set => scopes.Peek()[key] = value;
    }

    public void Add(TK key, TV value)
    {
      scopes.Peek().Add(key, value);
    }

    public bool ContainsKey(TK key)
    {
      foreach (Scope currentScope in scopes)
      {
        if (currentScope.ContainsKey(key))
        {
          return true;
        }
      }
      return false;
    }

    public void EnterScope()
    {
      scopes.Push(new Scope());
    }

    public void LeaveScope()
    {
      scopes.Pop();
    }

    public bool TryGetValue(TK key, out TV value)
    {
      foreach (Scope currentScope in scopes)
      {
        if (currentScope.TryGetValue(key, out value))
        {
          return true;
        }
      }
      value = default;
      return false;
    }

    private sealed class Scope : Dictionary<TK, TV>
    {
    }

    private sealed class Scopes : Stack<Scope>
    {
    }
  }
}