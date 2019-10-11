using System.IO;
using Eto.Drawing;
using Eto.Forms;

namespace Atom2
{
  public sealed class EtoFormsEditor : Form
  {
    private static readonly Application Application = new Application();

    private static readonly Font StandardFont = new Font("Verdana", 10);

    private EtoFormsEditor(string startProgramPath)
    {
      Title = "Hello World!";
      ClientSize = new Size(600, 600);
      Menu = new MenuBar {Items = {new ButtonMenuItem {Text = "&File", Items = {new Command((sender, e) => MessageBox.Show("Open")) {MenuText = "Open"}, new ButtonMenuItem {Text = "Click Me, MenuItem"}}}}};
      TableLayout layout = new TableLayout {Spacing = new Size(0, 0), Padding = new Padding(0, 0, 0, 0)};
      TableLayout middle = new TableLayout();
      middle.Rows.Add(new TableRow(new TableCell(new TextArea { Font = StandardFont, Text = Runtime.Code(startProgramPath)}, true)) {ScaleHeight = true});
      middle.Rows.Add(new TableRow(new TextArea()));
      layout.Rows.Add(new TableRow(new TextArea {Text = "Third Column"}, new TableCell(middle, true), new TextArea {Text = "Third Column"}));
      Content = layout;
    }

    public static void Run(string[] arguments)
    {
      Application.Run(new EtoFormsEditor(arguments[1]));
    }
  }
}