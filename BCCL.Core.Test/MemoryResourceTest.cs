using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BCCL.Core.UI;
using BCCL.Core.UI.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace BCCL.Core.Test;
[TestClass]
public class MemoryResourceTest
{
    private static void Sta(Action action) {
        Exception? error=null; var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}});
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error is not null) throw error;
    }
    private static void WriteImage(string path, int width) {
        var pixels=new byte[width*4]; for(var i=0;i<pixels.Length;i++) pixels[i]=255;
        var source=BitmapSource.Create(width,1,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var file=File.Create(path);encoder.Save(file);
    }
    [TestMethod] public void ImagesAreSharedFrozenAndRemainFullResolution() => Sta(()=>{
        var file=Path.GetTempFileName();try{WriteImage(file,777);var a=SharedImageSource.Load(file);var b=SharedImageSource.Load(file);Assert.AreSame(a,b);Assert.IsTrue(a.IsFrozen);Assert.AreEqual(777,a.PixelWidth);Assert.AreEqual(1,a.PixelHeight);}finally{File.Delete(file);}
    });
    [TestMethod] public void ReplacedImageInvalidatesCacheAndDoesNotLockFile() => Sta(()=>{
        var file=Path.GetTempFileName();try{WriteImage(file,2);var a=SharedImageSource.Load(file);WriteImage(file,19);File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddSeconds(2));var b=SharedImageSource.Load(file);Assert.AreNotSame(a,b);Assert.AreEqual(19,b.PixelWidth);File.Delete(file);Assert.IsFalse(File.Exists(file));}finally{File.Delete(file);}
    });
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference LoadWeak(string file) => new(SharedImageSource.Load(file));
    [TestMethod] public void ImageIndexDoesNotRetainUnusedPixelBuffers() => Sta(()=>{
        var file=Path.GetTempFileName();try{WriteImage(file,23);var weak=LoadWeak(file);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Assert.IsFalse(weak.IsAlive);}finally{File.Delete(file);}
    });
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference NewMotd() => new(new MotdRenderer());
    [TestMethod] public void UnusedMotdIsNotRootedByDispatcherTimer() => Sta(()=>{
        var weak=NewMotd();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Assert.IsFalse(weak.IsAlive);
    });
}
