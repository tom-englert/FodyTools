namespace ShellAssembly
{
    using System;

    using Microsoft.WindowsAPICodePack.Dialogs;

    using MS.WindowsAPICodePack.Internal;

    public class Program
    {
        public static void Main()
        {
            using (var dlg = new CommonOpenFileDialog { IsFolderPicker = true, InitialDirectory = ".", EnsurePathExists = true, DefaultFileName = "abc"})
            {
                if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    var x = new PropVariant();
                    var y = new PropVariant(true);

                    Console.WriteLine(x.VarType);
                    Console.WriteLine(y.VarType);

                    Console.ReadKey();
                }
            }
        }
    }
}
