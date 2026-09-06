using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GridBoard
{
    public static class GlobalAtomGuard
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern short GlobalAddAtom(string lpString);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern short GlobalDeleteAtom(short nAtom);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern short GlobalFindAtom(string lpString);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetLastError();

        private static short _atomId = 0;

        private const string APP_ID = "GRIDBOARD_APP_UNIQ_114514_1919810";
        public static void Clean()
        {
            short atom = GlobalFindAtom(APP_ID);

            if (atom != 0)
            {
                Console.WriteLine($"{atom}");
                short result = GlobalDeleteAtom(atom);
                Console.WriteLine(result != 0 ? "清理成功！" : "清理失败（可能被其他进程占用）");
            }
            else
            {
                Console.WriteLine("NOT FOUND");
            }
        }
        public static bool TryAcquire()
        {
            if (_atomId != 0)
            {
                return true;
            }

            short existingAtom = GlobalFindAtom(APP_ID);
            if (existingAtom != 0)
            {
                return false;
            }

            _atomId = GlobalAddAtom(APP_ID);
            if (_atomId == 0)
            {
                return false; 
            }

            short checkAtom = GlobalFindAtom(APP_ID);
            if (checkAtom != _atomId)
            {
                GlobalDeleteAtom(_atomId);
                _atomId = 0;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 释放原子锁（进程退出时调用）
        /// </summary>
        public static void Release()
        {
            if (_atomId != 0)
            {
                GlobalDeleteAtom(_atomId);
                _atomId = 0;
            }
        }

        /// <summary>
        /// 检查当前是否有实例在运行（不改变原子表）
        /// </summary>
        public static bool IsRunning()
        {
            return GlobalFindAtom(APP_ID) != 0;
        }
    }
}
