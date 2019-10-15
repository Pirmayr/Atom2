using System;

namespace Atom2
{
  public static class Program
  {
    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        Editor.Run(arguments);
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}