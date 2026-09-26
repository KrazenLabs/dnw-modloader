using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DnWModLoader
{
    internal static class FileDialog
    {
        private const uint CoinitApartmentThreaded = 0x2;
        private const uint ClsctxInprocServer = 0x1;
        private const int ErrorCancelled = unchecked((int)0x800704C7);
        private const uint SigdnFileSysPath = 0x80058000;

        private const uint FosNoChangeDir = 0x8;
        private const uint FosPickFolders = 0x20;
        private const uint FosForceFileSystem = 0x40;
        private const uint FosAllowMultiSelect = 0x200;
        private const uint FosPathMustExist = 0x800;
        private const uint FosFileMustExist = 0x1000;
        private const uint FosDontAddToRecent = 0x2000000;

        private const int ReleaseSlot = 2;
        private const int ShowSlot = 3;
        private const int SetFileTypesSlot = 4;
        private const int SetOptionsSlot = 9;
        private const int GetOptionsSlot = 10;
        private const int SetTitleSlot = 17;
        private const int SetOkButtonLabelSlot = 18;
        private const int GetResultsSlot = 27;
        private const int ItemArrayGetCountSlot = 7;
        private const int ItemArrayGetItemAtSlot = 8;
        private const int ItemGetDisplayNameSlot = 5;

        private static readonly Guid FileOpenDialogClass = new Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
        private static readonly Guid FileOpenDialogInterface = new Guid("d57c7288-d4ad-4768-be02-9d969532d960");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseMethod(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ShowMethod(IntPtr self, IntPtr owner);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetFileTypesMethod(IntPtr self, uint count, IntPtr filterSpecs);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetOptionsMethod(IntPtr self, uint options);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetOptionsMethod(IntPtr self, out uint options);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetTextMethod(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string text);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetResultsMethod(IntPtr self, out IntPtr items);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCountMethod(IntPtr self, out uint count);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetItemAtMethod(IntPtr self, uint index, out IntPtr item);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDisplayNameMethod(IntPtr self, uint form, out IntPtr name);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance([In] ref Guid classId, IntPtr outer, uint context, [In] ref Guid interfaceId, out IntPtr instance);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr memory);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        internal static IntPtr ActiveWindow()
        {
            try { return GetActiveWindow(); }
            catch (Exception) { return IntPtr.Zero; }
        }

        internal static string[] Pick(IntPtr owner, bool folders, string title, string filterName, string filterPatterns)
        {
            int initialized = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);
            if (initialized < 0) throw new InvalidOperationException("COM could not be set up for the dialog (0x" + initialized.ToString("X8") + ")");
            try
            {
                return Show(owner, folders, title, filterName, filterPatterns);
            }
            finally
            {
                CoUninitialize();
            }
        }

        private static string[] Show(IntPtr owner, bool folders, string title, string filterName, string filterPatterns)
        {
            IntPtr dialog = IntPtr.Zero, results = IntPtr.Zero, filterSpecs = IntPtr.Zero;
            var strings = new List<IntPtr>();
            try
            {
                Guid classId = FileOpenDialogClass, interfaceId = FileOpenDialogInterface;
                Check(CoCreateInstance(ref classId, IntPtr.Zero, ClsctxInprocServer, ref interfaceId, out dialog), "CoCreateInstance");

                uint options;
                Check(Method<GetOptionsMethod>(dialog, GetOptionsSlot)(dialog, out options), "GetOptions");
                options |= FosForceFileSystem | FosAllowMultiSelect | FosPathMustExist | FosNoChangeDir | FosDontAddToRecent;
                options |= folders ? FosPickFolders : FosFileMustExist;
                Check(Method<SetOptionsMethod>(dialog, SetOptionsSlot)(dialog, options), "SetOptions");

                if (!folders && !string.IsNullOrEmpty(filterPatterns))
                {
                    filterSpecs = Marshal.AllocHGlobal(2 * IntPtr.Size);
                    strings.Add(Marshal.StringToHGlobalUni(filterName ?? filterPatterns));
                    strings.Add(Marshal.StringToHGlobalUni(filterPatterns));
                    Marshal.WriteIntPtr(filterSpecs, 0, strings[0]);
                    Marshal.WriteIntPtr(filterSpecs, IntPtr.Size, strings[1]);
                    Check(Method<SetFileTypesMethod>(dialog, SetFileTypesSlot)(dialog, 1, filterSpecs), "SetFileTypes");
                }
                if (!string.IsNullOrEmpty(title)) Check(Method<SetTextMethod>(dialog, SetTitleSlot)(dialog, title), "SetTitle");
                Check(Method<SetTextMethod>(dialog, SetOkButtonLabelSlot)(dialog, "Add"), "SetOkButtonLabel");

                int shown = Method<ShowMethod>(dialog, ShowSlot)(dialog, owner);
                if (shown == ErrorCancelled) return new string[0];
                Check(shown, "Show");

                Check(Method<GetResultsMethod>(dialog, GetResultsSlot)(dialog, out results), "GetResults");
                uint count;
                Check(Method<GetCountMethod>(results, ItemArrayGetCountSlot)(results, out count), "GetCount");
                var paths = new List<string>();
                var getItemAt = Method<GetItemAtMethod>(results, ItemArrayGetItemAtSlot);
                for (uint i = 0; i < count; i++)
                {
                    IntPtr item;
                    if (getItemAt(results, i, out item) < 0 || item == IntPtr.Zero) continue;
                    try
                    {
                        IntPtr name;
                        if (Method<GetDisplayNameMethod>(item, ItemGetDisplayNameSlot)(item, SigdnFileSysPath, out name) < 0 || name == IntPtr.Zero) continue;
                        try { paths.Add(Marshal.PtrToStringUni(name)); }
                        finally { CoTaskMemFree(name); }
                    }
                    finally
                    {
                        Release(item);
                    }
                }
                return paths.ToArray();
            }
            finally
            {
                if (results != IntPtr.Zero) Release(results);
                if (dialog != IntPtr.Zero) Release(dialog);
                if (filterSpecs != IntPtr.Zero) Marshal.FreeHGlobal(filterSpecs);
                foreach (var text in strings) Marshal.FreeHGlobal(text);
            }
        }

        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(instance);
            IntPtr function = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(function, typeof(T));
        }

        private static void Release(IntPtr instance)
        {
            Method<ReleaseMethod>(instance, ReleaseSlot)(instance);
        }

        private static void Check(int result, string step)
        {
            if (result < 0) throw new InvalidOperationException(step + " failed (0x" + result.ToString("X8") + ")");
        }
    }
}
