namespace Atom2
{
  using Eto.Forms;
  using Eto.Drawing;

  public class EtoFormsEditor : Form
  {
    private static readonly Application application = new Application();

    public EtoFormsEditor()
    {
      Title = "Hello World!";
      ClientSize = new Size(600, 600);

      TableLayout layout = new TableLayout
      {
        Spacing = new Size(0, 0),
        Padding = new Padding(0, 0, 0, 0)
      };

      TableLayout middle = new TableLayout();

      middle.Rows.Add(new TableRow(new TableCell(new TextArea(), true)) { ScaleHeight = true });
      middle.Rows.Add(new TableRow(new TextArea()));

      layout.Rows.Add
      (
        new TableRow
        (
          new TextArea { Text = "Third Column" },
          new TableCell(middle, true),
          new TextArea { Text = "Third Column" }
        )
      );


      Content = layout;
    }

    public static void Run(string[] arguments)
    {
      application.Run(new EtoFormsEditor());
    }
  }
}
