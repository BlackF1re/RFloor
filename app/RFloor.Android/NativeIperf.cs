using System.Runtime.InteropServices;
namespace RFloor.Android;
internal static class NativeIperf {
    private static readonly SemaphoreSlim gate=new(1,1);
    [DllImport("rfloor_iperf",CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr rfloor_run([MarshalAs(UnmanagedType.LPUTF8Str)]string host,int port,int seconds,int reverse,[MarshalAs(UnmanagedType.LPUTF8Str)]string temporary);
    [DllImport("rfloor_iperf",CallingConvention=CallingConvention.Cdecl)] private static extern void rfloor_free(IntPtr value);
    [DllImport("rfloor_iperf",CallingConvention=CallingConvention.Cdecl)] private static extern void rfloor_cancel();
    internal static async Task<string> Run(string host,int port,int seconds,bool reverse,CancellationToken token){
        await gate.WaitAsync(token);
        try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(seconds+15));
            string temporary=System.IO.Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath,"iperf.XXXXXX");
            var operation=Task.Run(()=>{timeout.Token.ThrowIfCancellationRequested();IntPtr value=rfloor_run(host,port,seconds,reverse?1:0,temporary);if(value==IntPtr.Zero)throw new InvalidOperationException("iperf3 could not allocate a test.");try{return Marshal.PtrToStringUTF8(value)??"{}";}finally{rfloor_free(value);}},CancellationToken.None);
            using var cancel=timeout.Token.Register(()=>rfloor_cancel());string json=await operation;token.ThrowIfCancellationRequested();if(timeout.IsCancellationRequested)throw new TimeoutException("iperf3 timed out.");return json;
        }finally{gate.Release();}
    }
}
