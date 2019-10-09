﻿using System.Collections.Generic;
using System.Linq;

#pragma warning disable 618

namespace Atom2
{
  public sealed class ScopedDictionary<TK, TV>
  {
    private sealed class Scopes : Stack<Dictionary<TK, TV>> { }

    private readonly Scopes scopes = new Scopes();

    public ScopedDictionary()
    {
      EnterScope();
    }

    public void Add(TK key, TV value)
    {
      scopes.Peek().Add(key, value);
    }

    public bool ContainsKey(TK key)
    {
      foreach (Dictionary<TK, TV> currentScope in scopes)
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
      scopes.Push(new Dictionary<TK, TV>());
    }

    public void LeaveScope()
    {
      scopes.Pop();
    }

    public List<KeyValuePair<TK, TV>> ToList()
    {
      return scopes.Peek().ToList();
    }

    public bool TryGetValue(TK key, out TV value)
    {
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