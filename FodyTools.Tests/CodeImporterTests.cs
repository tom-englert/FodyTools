﻿namespace FodyTools.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;

    using FodyTools;

    using JetBrains.Annotations;

    using Mono.Cecil;

    using TomsToolbox.Core;

    using Xunit;

    public class CodeImporterTests
    {
        [Theory]
        [InlineData(typeof(Test<>))]
        public void SimpleTypesTest([NotNull] params Type[] types)
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);

            var governingType = types.First();

            Debug.Assert(module != null, nameof(module) + " != null");
            Debug.Assert(governingType?.Namespace != null, nameof(governingType) + " != null");

            var target = new CodeImporter(module, governingType.Namespace);

            var sourceAssemblyPath = governingType.Assembly.Location;

            var imported = target.Import(types);

            var tempPath = Path.GetTempPath();

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly1.dll");

            module.Write(targetAssemblyPath);

            foreach (var t in imported)
            {
                var decompiledSource = ILDasm.Decompile(sourceAssemblyPath, t.FullName);
                var decompiledTarget = ILDasm.Decompile(targetAssemblyPath, t.FullName);

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), decompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), decompiledTarget);

                Assert.Equal(decompiledSource, decompiledTarget);
            }
        }

        [Theory]
        [InlineData(typeof(WeakEventListener<,,>))]
        [InlineData(typeof(WeakEventSource<>))]
        [InlineData(typeof(WeakEventSource<>), typeof(WeakEventListener<,,>), typeof(Test<>))]
        public void ComplexTypesTest([NotNull] params Type[] types)
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);

            var governingType = types.First();

            Debug.Assert(module != null, nameof(module) + " != null");
            Debug.Assert(governingType?.Namespace != null, nameof(governingType) + " != null");

            var target = new CodeImporter(module, governingType.Namespace);

            var sourceAssemblyPath = governingType.Assembly.Location;

            var imported = target.Import(types);

            var tempPath = Path.GetTempPath();

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Write(targetAssemblyPath);

            foreach (var t in imported)
            {
                var decompiledSource = ILDasm.Decompile(sourceAssemblyPath, t.FullName);
                var decompiledTarget = ILDasm.Decompile(targetAssemblyPath, t.FullName);

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), decompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), decompiledTarget);

                // TODO: Does not work for complex types with dependencies...
                // Assert.Equal(decompiledSource, decompiledTarget);
            }
        }

        private static class ILDasm
        {
            private static readonly string _ilDasmPath = FindILDasm();

            [NotNull]
            public static string Decompile(string assemblyPath, string className)
            {
                var startInfo = new ProcessStartInfo(_ilDasmPath, $"\"{assemblyPath}\" /text /item:{className}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    return process?.StandardOutput.ReadToEnd() ?? "ILDasm did not start";
                }
            }

            [NotNull]
            private static string FindILDasm()
            {
                var windowsSdkDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft SDKs\\Windows");
                var path = Directory.EnumerateFiles(windowsSdkDirectory, "ILDasm.exe", SearchOption.AllDirectories)
                    .Where(item => item?.IndexOf("x64", StringComparison.OrdinalIgnoreCase) == -1)
                    .OrderByDescending(GetFileVersion)
                    .FirstOrDefault()
                    ?? throw new FileNotFoundException("ILDasm.exe");

                return path;
            }

            [NotNull]
            private static Version GetFileVersion([NotNull] string path)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                return new Version(versionInfo.FileMajorPart, versionInfo.FileMinorPart, versionInfo.FileBuildPart);
            }
        }
    }

    // ReSharper disable all (just test code below)

    [Some]
    internal class Test<T> : List<T> where T : EventArgs
    {
        private readonly EventHandler<T> _handler;
        private int _field;
        private EventHandler<T> _delegate;

        public Test(EventHandler<T> handler)
        {
            _handler = handler;
            _field = 0;
            _delegate = OnEvent;
        }

        public int Value()
        {
            return _field;
        }

        public void Add(int value)
        {
            _field += value;
        }

        public void OnEvent(object sender, T e)
        {

        }

        private Referenced GetReferenced()
        {
            return null;
        }

        public Referenced Referenced { get; set; }
    }

    internal class MyEventArgs : EventArgs, IEnumerable<string>
    {
        public string GetValue()
        {
            return null;
        }

        public IEnumerator<string> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal class Referenced : List<string>
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, int dwFlags);

        private readonly Test<MyEventArgs> _owner;
        private readonly IntPtr _hlib;

        public Referenced(Test<MyEventArgs> owner)
        {
            _owner = owner;
            _hlib = LoadLibraryEx("dummy", IntPtr.Zero, 0);
        }

        public Test<MyEventArgs> Owner
        {
            get
            {
                return _owner;
            }
        }

        public void WithArgs<T>(T value) where T : class, IList
        {

        }
    }

    [AttributeUsage(AttributeTargets.All)]
    internal class SomeAttribute : Attribute
    {

    }
}