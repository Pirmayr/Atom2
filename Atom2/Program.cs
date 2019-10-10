using System;

namespace Atom2
{
  public class Program
  {
    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        EtoFormsEditor.Run(arguments);

        // WinformsEditor.Run(arguments);

      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}
