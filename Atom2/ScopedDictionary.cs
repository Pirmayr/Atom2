using System.Collections.Generic;

namespace Atom2
{
  public class ScopedDictionary<TK, TV>
  {
    private sealed class Scopes : Stack<Dictionary<TK, TV>>
    {
    }

    private readonly Scopes scopes = new Scopes();

    protected ScopedDictionary()
    {
      EnterScope();
    }

    public void Add(TK key, TV value)
    {
      scopes.Peek().Add(key, value);
    }

    public void EnterScope()
    {
      scopes.Push(new Dictionary<TK, TV>());
    }

    public void LeaveScope()
    {
      scopes.Pop();
    }

    public bool TryGetValue(TK key, out TV value)
    {
      if (0 < scopes.Count && key.Equals("abc"))
      {
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        int a = 0;
#pragma warning restore CS0219 // Variable is assigned but its value is never used
      }

      foreach (Dictionary<TK, TV> currentScope in scopes)
      {
        if (currentScope.TryGetValue(key, out value))
        {
          return true;
        }
      }
      value = default(TV);
      return false;
    }

    public TV this[TK key]
    {
      get => TryGetValue(key, out TV result) ? result : default(TV);
      set => scopes.Peek()[key] = value;
    }
  }
}