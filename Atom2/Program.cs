using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Atom2
{
    class MainClass
    {

        public static void Main(string[] args)
        {
            try
            {
                new Atom2.System().Run(File.ReadAllText(args[0]));
                Console.Write("done");
            }
            catch (Exception exception)
            {
                Console.Write(exception.Message);
            }
        }
    }
}
