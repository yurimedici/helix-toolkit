﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DPFCanvas.cs" company="Helix Toolkit">
//   Copyright (c) 2018 Helix Toolkit contributors
// </copyright>
// <summary>
//
// </summary>
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using global::SharpDX;
using global::SharpDX.Direct3D11;
#if DX11_1
    using Device = SharpDX.Direct3D11.Device1;
    using DeviceContext = SharpDX.Direct3D11.DeviceContext1;
#else
    using Device = SharpDX.Direct3D11.Device;
#endif
namespace HelixToolkit.Wpf.SharpDX
{
    using Controls;
    using Render;
    using Utilities;

    // ---- BASED ON ORIGNAL CODE FROM -----
    // Copyright (c) 2010-2012 SharpDX - Alexandre Mutel
    // 
    // Permission is hereby granted, free of charge, to any person obtaining a copy
    // of this software and associated documentation files (the "Software"), to deal
    // in the Software without restriction, including without limitation the rights
    // to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    // copies of the Software, and to permit persons to whom the Software is
    // furnished to do so, subject to the following conditions:
    // 
    // The above copyright notice and this permission notice shall be included in
    // all copies or substantial portions of the Software.
    // 
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    // THE SOFTWARE.

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="System.Windows.Controls.Image" />
    public class DPFCanvas : Image, IRenderCanvas
    {
        /// <summary>
        /// Gets a value indicating whether the control is in design mode
        /// (running in Blend or Visual Studio).
        /// </summary>
        public static bool IsInDesignMode
        {
            get
            {
                var prop = DesignerProperties.IsInDesignModeProperty;
                return (bool)DependencyPropertyDescriptor.FromProperty(prop, typeof(FrameworkElement)).Metadata.DefaultValue;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        static DPFCanvas()
        {
            StretchProperty.OverrideMetadata(typeof(DPFCanvas), new FrameworkPropertyMetadata(Stretch.Fill));
        }

        /// <summary>
        /// Gets or sets the render host.
        /// </summary>
        /// <value>
        /// The render host.
        /// </value>
        public IRenderHost RenderHost { private set; get; }
        private Window parentWindow;

        /// <summary>
        /// Fired whenever an exception occurred on this object.
        /// </summary>
        public event EventHandler<RelayExceptionEventArgs> ExceptionOccurred = delegate { };

        private readonly CompositionTargetEx compositionTarget = new CompositionTargetEx();

        /// <summary>
        /// 
        /// </summary>
        public DPFCanvas(bool deferredRendering = false)
        {
            if (deferredRendering)
            {
                RenderHost = new DX11ImageSourceRenderHost((device) => { return new DeferredContextRenderer(device, new AutoRenderTaskScheduler()); });
            }
            else
            {
                RenderHost = new DX11ImageSourceRenderHost();
            }
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            RenderHost.StartRenderLoop += RenderHost_StartRenderLoop;
            RenderHost.StopRenderLoop += RenderHost_StopRenderLoop;
            RenderHost.ExceptionOccurred += (s, e) => { HandleExceptionOccured(e.Exception); };
            (RenderHost as DX11ImageSourceRenderHost).OnImageSourceChanged += DPFCanvas_OnImageSourceChanged;
        }

        private void DPFCanvas_OnImageSourceChanged(object sender, DX11ImageSourceArgs e)
        {
            this.Source = e.Source;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (IsInDesignMode)
            {
                return;
            }
            try
            {
                parentWindow = FindVisualAncestor<Window>(this);
                if (parentWindow != null)
                {
                    parentWindow.Closed -= ParentWindow_Closed;
                    parentWindow.Closed += ParentWindow_Closed;
                }
                StartD3D();
            }
            catch (Exception ex)
            {
                // Exceptions in the Loaded event handler are silently swallowed by WPF.
                // https://social.msdn.microsoft.com/Forums/vstudio/en-US/9ed3d13d-0b9f-48ac-ae8d-daf0845c9e8f/bug-in-wpf-windowloaded-exception-handling?forum=wpf
                // http://stackoverflow.com/questions/19140593/wpf-exception-thrown-in-eventhandler-is-swallowed
                // tl;dr: M$ says it's "by design" and "working as indended" but may change in the future :).

                if (!HandleExceptionOccured(ex))
                {
                    MessageBox.Show(string.Format("DPFCanvas: Error starting rendering: {0}", ex.Message), "Error");
                }
            }
        }

        private void ParentWindow_Closed(object sender, EventArgs e)
        {
            Source = null;
            EndD3D();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (IsInDesignMode)
            {
                return;
            }
            if(parentWindow != null)
            {
                parentWindow.Closed -= ParentWindow_Closed;
            }
            EndD3D();
        }

        /// <summary>
        /// 
        /// </summary>
        private bool StartD3D()
        {                   
            RenderHost.StartD3D(ActualWidth, ActualHeight);
            return true;
        }

        private void RenderHost_StopRenderLoop(object sender, EventArgs e)
        {
            compositionTarget.Rendering -= CompositionTargetEx_Rendering;
        }

        private void RenderHost_StartRenderLoop(object sender, EventArgs e)
        {
            compositionTarget.Rendering -= CompositionTargetEx_Rendering;
            compositionTarget.Rendering += CompositionTargetEx_Rendering;
        }

        private void CompositionTargetEx_Rendering(object sender, RenderingEventArgs e)
        {
            RenderHost.UpdateAndRender();
        }
        private TimeSpan _last = TimeSpan.Zero;
        /// <summary>
        /// 
        /// </summary>
        private void EndD3D()
        {
            RenderHost.EndD3D();           
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnIsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Debug.WriteLine($"OnIsFrontBufferAvailableChanged: {(bool)e.NewValue}");
            // this fires when the screensaver kicks in, the machine goes into sleep or hibernate
            // and any other catastrophic losses of the d3d device from WPF's point of view
            if (true.Equals(e.NewValue))
            {
                try
                {
                    // Try to recover from DeviceRemoved/DeviceReset
                    EndD3D();
                    StartD3D();
                }
                catch (Exception ex)
                {
                    if (!HandleExceptionOccured(ex))
                    {
                        MessageBox.Show(string.Format("DPFCanvas: Error while rendering: {0}", ex.Message), "Error");
                    }
                }
            }
        }

        private DispatcherOperation resizeOperation = null;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sizeInfo"></param>
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (resizeOperation != null && resizeOperation.Status == DispatcherOperationStatus.Pending)
            {
                resizeOperation.Abort();
            }
            resizeOperation = Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
            {
                if (IsLoaded)
                {
                    try
                    {
                        RenderHost.Resize(ActualWidth, ActualHeight);                       
                    }
                    catch (Exception ex)
                    {
                        if (!HandleExceptionOccured(ex))
                        {
                            MessageBox.Show(string.Format("DPFCanvas: Error while rendering: {0}", ex.Message), "Error");
                        }
                    }
                }
            }));
        }

        /// <summary>
        /// Invoked whenever an exception occurs. Stops rendering, frees resources and throws 
        /// </summary>
        /// <param name="exception">The exception that occured.</param>
        /// <returns><c>true</c> if the exception has been handled, <c>false</c> otherwise.</returns>
        private bool HandleExceptionOccured(Exception exception)
        {
            EndD3D();

            var sdxException = exception as SharpDXException;
            if (sdxException != null &&
                (sdxException.Descriptor == global::SharpDX.DXGI.ResultCode.DeviceRemoved ||
                 sdxException.Descriptor == global::SharpDX.DXGI.ResultCode.DeviceReset))
            {
                // Try to recover from DeviceRemoved/DeviceReset
                StartD3D();
                return true;
            }
            else
            {
                var args = new RelayExceptionEventArgs(exception);
                ExceptionOccurred(this, args);
                return args.Handled;
            }
        }

        public static T FindVisualAncestor<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj != null)
            {
                var parent = System.Windows.Media.VisualTreeHelper.GetParent(obj);
                while (parent != null)
                {
                    var typed = parent as T;
                    if (typed != null)
                    {
                        return typed;
                    }

                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                }
            }

            return null;
        }
    }
}
