using System;

namespace Atom2
{
  public static class Program
  {
    public static Runtime Runtime { get; private set; }

    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        Runtime = new Runtime(arguments[0]);
        Editor.Run(arguments);
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}