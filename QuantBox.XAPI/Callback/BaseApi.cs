﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace QuantBox.XAPI.Callback
{
    public class BaseApi : IDisposable
    {
        protected Proxy proxy;
        protected IntPtr Handle = IntPtr.Zero;
        protected Queue _Queue;
        private string _Path;

        protected XCall _XRespone;

        protected object locker = new object();

        public bool IsConnected { get; protected set; }
        private int _reconnectInterval;
        public int ReconnectInterval
        {
            get { return _reconnectInterval; }
            set {
                _reconnectInterval = value;
                _Timer.Elapsed -= _Timer_Elapsed;
                if (_reconnectInterval >= 10)
                {
                    _Timer.Elapsed += _Timer_Elapsed;
                    _Timer.Interval = _reconnectInterval*1000;
                }
                _Timer.Enabled = _reconnectInterval >= 10;
            }
        }

        public ServerInfoField Server;
        public UserInfoField User;

        public DelegateOnConnectionStatus OnConnectionStatus;
        public DelegateOnRtnError OnRtnError;

        public RspUserLoginField UserLoginField;


        private System.Timers.Timer _Timer = new System.Timers.Timer();

        public BaseApi(string path1,Queue queue)
        {
            _Path = path1;
            
            // 这里是因为回调函数可能被GC回收
            _XRespone = _OnRespone;
            _Queue = queue;

            ReconnectInterval = 0;
        }

        void _Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // 定时检查是否需要销毁对象重连
            if(!IsConnected)
            {
                Disconnect();
                Connect();
            }
        }


        ~BaseApi()
        {
            Dispose(false);
        }

        private bool disposed;

        // 一定要先调用API的，再调用队列的，否则会出错
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Free other state (managed objects).
                }
                // Free your own state (unmanaged objects).
                // Set large fields to null.
                Disconnect();
                disposed = true;
            }
            //base.Dispose(disposing);
        }

        public virtual void Connect()
        {
            lock(locker)
            {
                // XSpeed多次连接出问题，发现会生成多次
                if (proxy != null)
                    return;

                proxy = new Proxy(_Path);

                Handle = proxy.XRequest((byte)RequestType.Create, IntPtr.Zero, IntPtr.Zero, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, 0);

                // 将API与队列进行绑定
                proxy.XRequest((byte)RequestType.Register, Handle, IntPtr.Zero, 0, 0, _Queue.Handle, 0, IntPtr.Zero, 0, IntPtr.Zero, 0);

                _Queue.Register(Marshal.GetFunctionPointerForDelegate(_XRespone));
                // 启动队列循环
                _Queue.Connect();

                IntPtr ServerIntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ServerInfoField)));
                Marshal.StructureToPtr(Server, ServerIntPtr, false);

                IntPtr UserIntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UserInfoField)));
                Marshal.StructureToPtr(User, UserIntPtr, false);

                // 进行连接
                proxy.XRequest((byte)RequestType.Connect, Handle, IntPtr.Zero, 0, 0, ServerIntPtr, 0, UserIntPtr, 0, Marshal.StringToHGlobalAnsi(Path.GetTempPath()), 0);

                Marshal.FreeHGlobal(ServerIntPtr);
                Marshal.FreeHGlobal(UserIntPtr);
            }
        }

        public virtual void Disconnect()
        {
            lock(locker)
            {
                IsConnected = false;

                if (proxy != null)
                {
                    // 将API与队列解绑定
                    proxy.XRequest((byte)RequestType.Register, Handle, IntPtr.Zero, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, 0);
                    proxy.XRequest((byte)RequestType.Release, Handle, IntPtr.Zero, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, 0);

                    proxy.Dispose();
                }
                
                proxy = null;
                Handle = IntPtr.Zero;
            }
        }

        private IntPtr _OnRespone(byte type, IntPtr pApi1, IntPtr pApi2, double double1, double double2, IntPtr ptr1, int size1, IntPtr ptr2, int size2, IntPtr ptr3, int size3)
        {
            // 队列过来的消息，如何处理？
            switch((ResponeType)type)
            {
                case ResponeType.OnConnectionStatus:
                    _OnConnectionStatus(double1, ptr1,size1);
                    break;
                case ResponeType.OnRtnError:
                    _OnRtnError(ptr1);
                    break;
                default:
                    return OnRespone(type, pApi1, pApi2, double1, double2, ptr1, size1, ptr2, size2, ptr3, size3);
            }

            return IntPtr.Zero;
        }

        protected virtual IntPtr OnRespone(byte type, IntPtr pApi1, IntPtr pApi2, double double1, double double2, IntPtr ptr1, int size1, IntPtr ptr2, int size2, IntPtr ptr3, int size3)
        {
            return IntPtr.Zero;
        }

        private void _OnConnectionStatus(double double1, IntPtr ptr1, int size1)
        {
            ConnectionStatus status = (ConnectionStatus)double1;
            // 连接状态更新
            IsConnected = (ConnectionStatus.Done == status);

            RspUserLoginField obj = default(RspUserLoginField);

            switch(status)
            {
                case ConnectionStatus.Logined:
                case ConnectionStatus.Disconnected:
                case ConnectionStatus.Doing:
                    obj = PInvokeUtility.GetObjectFromIntPtr<RspUserLoginField>(ptr1);
                    UserLoginField = obj;
                    break;
                default:
                    break;
            }

            if (OnConnectionStatus != null)
                OnConnectionStatus(this, status, ref obj, size1);
        }

        private void _OnRtnError(IntPtr ptr1)
        {
            if (OnRtnError == null)
                return;

            ErrorField obj = PInvokeUtility.GetObjectFromIntPtr<ErrorField>(ptr1);
            
            OnRtnError(this, ref obj);
        }
    }
}