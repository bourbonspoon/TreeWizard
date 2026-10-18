using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TreeWizard.App
{
    /// <summary>
    /// How many screen pixels make one UI pixel, the way native apps size their UI: 2 on a Retina Mac,
    /// 1.5 on Windows at 150%. Unity's Screen.dpi can't be used for this on macOS, where it reports
    /// 81 on a 5K Retina display, so there we ask the window for its backing scale instead. That also
    /// follows the display's "looks like" setting in System Settings and moves with the window between screens.
    /// </summary>
    public static class DisplayScale
    {
        public static float System
        {
            get
            {
                if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
                    return MacBackingScale() is float mac && mac > 0f ? mac : 1f;
                return Screen.dpi > 0f ? Screen.dpi / 96f : 1f;
            }
        }

        // [[NSApp mainWindow] backingScaleFactor], or the main screen's before the window exists.
        static float? MacBackingScale()
        {
            try
            {
                var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                var window = app != IntPtr.Zero ? Send(app, sel_registerName("mainWindow")) : IntPtr.Zero;
                var target = window != IntPtr.Zero ? window : Send(objc_getClass("NSScreen"), sel_registerName("mainScreen"));
                return target != IntPtr.Zero ? (float)SendDouble(target, sel_registerName("backingScaleFactor")) : (float?)null;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return null;
            }
        }

        const string ObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjC)]
        static extern IntPtr objc_getClass(string name);

        [DllImport(ObjC)]
        static extern IntPtr sel_registerName(string name);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        // A double comes back in a float register on both arm64 and x86_64, so plain objc_msgSend works.
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        static extern double SendDouble(IntPtr receiver, IntPtr selector);
    }
}
