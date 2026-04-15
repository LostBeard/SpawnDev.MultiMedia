using System.Runtime.InteropServices;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// DirectShow COM interop for video device enumeration.
    /// DirectShow finds virtual cameras (OBS, ManyCam, etc.) that
    /// MFEnumDeviceSources may miss.
    /// </summary>
    internal static class DShow
    {
        // CLSID_SystemDeviceEnum - the device enumerator
        public static readonly Guid CLSID_SystemDeviceEnum =
            new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");

        // CLSID_VideoInputDeviceCategory - all video capture devices
        public static readonly Guid CLSID_VideoInputDeviceCategory =
            new("860BB310-5D01-11D0-BD3B-00A0C911CE86");

        // CLSID_AudioInputDeviceCategory - all audio capture devices
        public static readonly Guid CLSID_AudioInputDeviceCategory =
            new("33D9A762-90C8-11D0-BD43-00A0C911CE86");

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern int CoCreateInstance(
            [In] ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    // COM Interface: ICreateDevEnum
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(
            [In] ref Guid clsidDeviceClass,
            [MarshalAs(UnmanagedType.Interface)] out IEnumMoniker ppEnumMoniker,
            uint dwFlags);
    }

    // COM Interface: IEnumMoniker
    [ComImport, Guid("00000102-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumMoniker
    {
        [PreserveSig]
        int Next(uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
            IMoniker[] rgelt,
            out uint pceltFetched);

        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumMoniker ppEnum);
    }

    // COM Interface: IMoniker (minimal - we only need BindToStorage for IPropertyBag)
    [ComImport, Guid("0000000F-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMoniker
    {
        // IUnknown (implicit)
        // IPersist
        [PreserveSig] int GetClassID(out Guid pClassID);
        // IPersistStream (4 methods)
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load(IntPtr pStm);
        [PreserveSig] int Save(IntPtr pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
        [PreserveSig] int GetSizeMax(out long pcbSize);
        // IMoniker
        [PreserveSig] int BindToObject(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riidResult, [MarshalAs(UnmanagedType.IUnknown)] out object ppvResult);
        [PreserveSig] int BindToStorage(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObj);
        // Remaining IMoniker methods - we don't use them, but they exist in the vtable
        [PreserveSig] int Reduce(IntPtr pbc, uint dwReduceHowFar, ref IntPtr ppmkToLeft, [MarshalAs(UnmanagedType.Interface)] out IMoniker ppmkReduced);
        [PreserveSig] int ComposeWith([MarshalAs(UnmanagedType.Interface)] IMoniker pmkRight, [MarshalAs(UnmanagedType.Bool)] bool fOnlyIfNotGeneric, [MarshalAs(UnmanagedType.Interface)] out IMoniker ppmkComposite);
        [PreserveSig] int Enum([MarshalAs(UnmanagedType.Bool)] bool fForward, [MarshalAs(UnmanagedType.Interface)] out IEnumMoniker ppenumMoniker);
        [PreserveSig] int IsEqual([MarshalAs(UnmanagedType.Interface)] IMoniker pmkOtherMoniker);
        [PreserveSig] int Hash(out uint pdwHash);
    }

    // COM Interface: IPropertyBag (used to read device FriendlyName and DevicePath)
    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyBag
    {
        [PreserveSig]
        int Read(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
            [MarshalAs(UnmanagedType.Struct)] out object pVar,
            IntPtr pErrorLog);

        [PreserveSig]
        int Write(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
            [MarshalAs(UnmanagedType.Struct)] ref object pVar);
    }
}
