namespace ShellAssembly
{
    using Microsoft.WindowsAPICodePack.Dialogs;

    public class Class1
    {
        public Class1()
        {
            var dlg = new CommonOpenFileDialog("Test");

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {

            }
        }
    }
}
