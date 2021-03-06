﻿using System;
using System.IO;
using System.Text;
using FileSysTemp.FSBase;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileSysConsole
{
    public class Execute
    {
        public MemoryUser sys_current_user = null;//当前登录用户，登录写好要后修改current_user！！！！！！！！！TODO
        public SuperBlock sys_sb = new SuperBlock();//超级块
        public iNodeTT sys_inode_tt = new iNodeTT();
        const uint MAX_USERNUM = 10; //内存中允许的最大用户数（同时在线）
        uint cur_usernum = 0;        //当前内存中驻留的用户数量
        /// <summary>
        /// 用于检查各属性项是否建立了索引
        /// </summary>
        Dictionary<string, bool> isCreateIndex = new Dictionary<string, bool>();
        /// <summary>
        /// 回收站文件地址映射（用于还原）List<Dictionary<inode_id, fore_addr_id>>
        /// </summary>
        Dictionary<uint, uint> recyclebinMap = new Dictionary<uint, uint>();

        public Execute()
        {
            isCreateIndex["id"] = false;
            isCreateIndex["name"] = false;
            isCreateIndex["size"] = false;
            isCreateIndex["uid"] = false;
            isCreateIndex["fore_addr"] = false;
            isCreateIndex["t_create"] = false;
            isCreateIndex["t_revise"] = false;
            isCreateIndex["type"] = false;
        }


        /// <summary>
        /// 输出所有i节点表
        /// </summary>
        public void OutputTT()
        {
            for (int i = 0; i < 128; i++)
            {
                Console.Write(i);
                Console.Write(":");
                for (int j = 0; sys_inode_tt.tt[i] != null && j < sys_inode_tt.tt[i].di_table.Count(); j++)
                {
                    Console.Write(sys_inode_tt.tt[i].di_table[j].id);
                }
                Console.WriteLine("");
            }
        }
        /// <summary>
        /// 用户登录文件系统
        /// </summary>
        /// <param name="uid">用户id</param>
        /// <param name="password">用户密码</param>
        /// <returns>登录是否成功</returns>
        public bool LoginSys()
        {
            Console.Write("UserID: ");
            uint uid;
            string uid_input, password;
            uid_input = Console.ReadLine();
            Console.Write("Password: ");
            password = Console.ReadLine();
            try
            {
                uid = Convert.ToUInt32(uid_input);
            }
            catch
            {
                Console.WriteLine("Invalid format, please check your input");
                return false;
            }
            List<User> users = LoadUsersInfofromDisk();
            bool isExist = false;
            User curUser = new User();
            foreach (User user in users)
            {
                if (user.uid == uid)
                {
                    isExist = true;
                    curUser.uid = user.uid;
                    curUser.password = user.password;
                    curUser.current_folder = user.current_folder;
                    break;
                }
            }
            if (!isExist)
            {
                //用户不存在
                Console.WriteLine("This account is not available, please check whether user " + uid.ToString() + " exists or not!");
                return false;
            }
            if (curUser.password == password)
            {
                //密码输入正确
                if (cur_usernum < MAX_USERNUM)
                {
                    //内存中同时在线用户数少于最大用户数限制，用户可以正常登录
                    sys_current_user = new MemoryUser(curUser.uid, curUser.current_folder, curUser.password);
                    cur_usernum++;
                    Console.WriteLine("Login successfully!");
                }
                else
                {
                    Console.WriteLine("Too much users in the system, waited to login!");
                    return false;
                }
            }
            else
            {
                //密码输入错误
                Console.WriteLine("Incorrect password, Login Failure!");
                return false;
            }
            //正常退出，用户登录成功！
            return true;
        }

        /// <summary>
        /// 退出文件系统
        /// </summary>
        /// <param name="uid">用户id</param>
        /// <returns>退出成功与否</returns>
        public bool LogoutSys()
        {
            try
            {
                UpdateDiskSFi();
                sys_current_user.Destructor(); //释放资源
                sys_current_user = null;
                cur_usernum--;
                Console.WriteLine("You have been logout successfully!");
                return true;
            }
            catch(Exception ex)
            {
                Console.WriteLine("Fail to logout! Exception Infomation: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 修改用户密码
        /// </summary>
        public bool RevisePassword()
        {
            string password, confirmpwd;
            do
            {
                Console.Write("New Password: ");
                password = Console.ReadLine();
                Console.Write("Confirm Password: ");
                confirmpwd = Console.ReadLine();
                if (password != confirmpwd)
                {
                    Console.WriteLine("The passwords input are inconsistent, please re-enter!");
                }
            } while (password != confirmpwd);
            if (sys_current_user == null)
            {
                Console.WriteLine("Please login your account first, and then change the password!");
                return false;
            }
            sys_current_user.newpassword = password;
            bool issuccess = StoreUserInfotoDisk(sys_current_user); //将更改写回磁盘
            Console.WriteLine("Password has been reset successfully!");
            return issuccess;
        }

        /// <summary>
        /// 将用户信息写回到磁盘
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="curfolder"></param>
        /// <returns></returns>
        public bool StoreUserInfotoDisk(MemoryUser currentUser)
        {
            List<User> userlist = LoadUsersInfofromDisk();
            for(int i=0;i<userlist.Count();i++)
            {
                if(currentUser.uid==userlist[i].uid)
                {
                    //修改当前用户的当前工作文件夹
                    userlist[i].current_folder = currentUser.current_folder;
                    //更新密码
                    userlist[i].password = currentUser.newpassword;
                    //写回磁盘
                    FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
                    BinaryFormatter binFormat = new BinaryFormatter();
                    fs.Position = SuperBlock.USER_DISK_START * SuperBlock.BLOCK_SIZE;
                    binFormat.Serialize(fs, userlist);
                    fs.Close();
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// 从磁盘用户区加载所有用户信息到内存
        /// </summary>
        /// <returns></returns>
        public List<User> LoadUsersInfofromDisk()
        {
            FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            BinaryFormatter binFormat = new BinaryFormatter();
            fs.Position = SuperBlock.USER_DISK_START * SuperBlock.BLOCK_SIZE;
            List<User> userslist = (List<User>)binFormat.Deserialize(fs);
            fs.Close();
            return userslist;
        }
        /// <summary>
        /// 启动文件系统，读取超级块和i节点
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            //1，登录
            if (LoginSys() == true)
            {
                //2，读取必要块区到内存
                FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
                BinaryFormatter binf = new BinaryFormatter();
                //读取超级块
                fs.Position = SuperBlock.SB_DISK_START * SuperBlock.BLOCK_SIZE;
                sys_sb = (SuperBlock)binf.Deserialize(fs);
                //读取i节点
                fs.Position = SuperBlock.iNODE_DISK_START * SuperBlock.BLOCK_SIZE;
                sys_inode_tt = (iNodeTT)binf.Deserialize(fs);
                fs.Close();
                //3，校验所读数据
                if (sys_sb.check_byte == 707197)
                {
                    Console.WriteLine("Boot FileSystem Successfully!");
                    return true;
                }
                else
                {
                    Console.WriteLine("Boot FileSystem Failed: Check Failed!");
                    return false;
                }
            }
            else
            {
                Console.WriteLine("Boot FileSystem Failed: Login Failed!");
                return false;
            }
        }
        /// <summary>
        /// 更新磁盘的超级块或i节点，默认全写回(true,true)，第一个参数决定超级块是否写回，第二个是i节点
        /// </summary>
        /// <param name="sb"></param>
        /// <param name="inode"></param>
        /// <returns></returns>
        public bool UpdateDiskSFi(bool sb = true, bool inode = true)
        {
            FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            BinaryFormatter binFormat = new BinaryFormatter();
            if (sb)
            {
                fs.Position = SuperBlock.SB_DISK_START * SuperBlock.BLOCK_SIZE;//超级块区
                binFormat.Serialize(fs, sys_sb);
            }
            if (inode)
            {
                fs.Position = SuperBlock.iNODE_DISK_START * SuperBlock.BLOCK_SIZE;//i节点区
                binFormat.Serialize(fs, sys_inode_tt);
            }
            fs.Close();
            return true;
        }
        /// <summary>
        /// 分配i节点ID，正常则返回i节点ID，错误则返回0
        /// </summary>
        /// <returns></returns>
        public uint AllocAiNodeID()
        {
            uint inode_id = 0;
            if (sys_sb.max_inode_id < uint.MaxValue - 1 && sys_sb.max_inode_id >= 100)
            {
                inode_id = sys_sb.max_inode_id;
                sys_sb.max_inode_id++;
                UpdateDiskSFi(true, false);//立即更新超级块磁盘数据
            }
            return inode_id;
        }

        /// <summary>
        /// 输入ID，返回i节点结构，错误则i节点name为.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public DiskiNode GetiNode(uint id)
        {
            uint temp_id = id % 128;
            DiskiNode dn = new DiskiNode(0,".",0);
            iNodeTable it = sys_inode_tt.tt[temp_id];
            for (int i = 0; i < it.di_table.Count(); i++)
            {
                if (it.di_table[i].id == id)
                {
                    dn = it.di_table[i];
                    return dn;
                }
            }
            return dn;
        }
        /// <summary>
        /// DirectOp里的副本，重构时可以删掉
        /// </summary>
        /// <param name="src"></param>
        /// <param name="tar"></param>
        /// <returns></returns>
        public bool MatchString(string src, string tar)
        {
            string temp1 = tar.Replace(".", @"\.");
            string temp = "^" + temp1.Replace("~", ".+") + "$";
            Regex reg = new Regex(@temp);
            if (reg.IsMatch(src))
                return true;
            else
                return false;
        }

        /// <summary>
        /// 输入路径，返回i节点结构
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public List<DiskiNode> GetiNodeByPath(string path)
        {
            uint temp_id = sys_current_user.current_folder;
            List<DiskiNode> dn_head = new List<DiskiNode>();
            List<DiskiNode> dn_tail = new List<DiskiNode>();
            DiskiNode err_dn = new DiskiNode(0, ".", 0);
            string[] paths0;
            List<string> paths = new List<string>();
            //若为绝对路径
            if (path[0] == '/')
            {
                temp_id = 0;
                paths0 = path[1..].Split(new char[] { '/' });
            }
            //若为相对路径
            else { paths0 = path.Split(new char[] { '/' }); }
            DiskiNode temp_dn = GetiNode(temp_id);
            dn_head.Add(temp_dn);
            dn_tail.Add(temp_dn);
            //去空，如/usr//ui/
            for(int i=0;i<paths0.Length;i++)
            {
                if(paths0[i].Length!=0)
                {
                    paths.Add(paths0[i]);
                }
            }
            //对每一级名字解析
            for (int i = 0; i < paths.Count(); i++)
            {
                //Console.WriteLine("GetiNodeByPath1:" + paths[i]);
                //本级不动
                if (paths[i] == ".") { }
                //返回上一级
                else if(paths[i] == "..")
                {
                    dn_head.Clear();
                    //把当前级的结果遍历
                    for(int j=0;j<dn_tail.Count();j++)
                    {
                        temp_dn = GetiNode(dn_tail[j].fore_addr);
                        bool has_exist = false;
                        //当前级的每个i节点的上一级是否已经加到了新结果里
                        for (int k = 0; k < dn_head.Count(); k++)
                        {
                            if (temp_dn.id == dn_head[k].id)
                            {
                                has_exist = true;
                                break;
                            }
                        }
                        if (!has_exist)
                            dn_head.Add(temp_dn);
                    }
                }
                //正常的符合正则表达式的名字
                else
                {
                    dn_head.Clear();
                    //遍历上一级每一条路径
                    for (int j=0;j<dn_tail.Count();j++)
                    {
                        //Console.WriteLine("dn_tail[j].type/name:" + dn_tail[j].type + dn_tail[j].name);
                        //还没到最后就匹配了文件，忽略这一条路
                        if (dn_tail[j].type==ItemType.FILE && i != paths.Count() - 1)
                        {
                            //Console.WriteLine("IGNORE");
                        }
                        //重大错误，根本不应该出现，要是遇到直接返回错误
                        else if (dn_tail[j].next_addr == null)
                        {
                            Console.WriteLine("ERROR AT GetiNodeByPath: NO THIS FILE/FOLDER");
                            dn_head.Clear();
                            dn_head.Add(err_dn);
                            return dn_head;
                        }
                        //正常地匹配到了文件夹
                        else
                        {
                            for(int k=0;k<dn_tail[j].next_addr.Count();k++)
                            {
                                temp_dn = GetiNode(dn_tail[j].next_addr[k]);
                                //Console.WriteLine("GetiNodeByPath2:" + temp_dn.name);
                                if (MatchString(temp_dn.name, paths[i]))
                                    dn_head.Add(temp_dn);
                            }
                        }
                    }
                }
                //更新结果
                dn_tail.Clear();
                for(int m=0;m<dn_head.Count();m++)
                {
                    dn_tail.Add(dn_head[m]);
                }
            }
            return dn_head;
        }

        /// <summary>
        /// 判断命名是否冲突，冲突返回true
        /// </summary>
        /// <param name="current_folder"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public bool IsNameConflict(DiskiNode fold_node,string name,ItemType type)
        {
            //判断新文件(夹)名是否为空
            if (name.Length == 0) { Console.WriteLine("File/Folder's name is empty!"); return false; }
            //判断是否有同名文件(夹)，注意没有文件的可能
            for (int i = 0; fold_node.next_addr != null && i < fold_node.next_addr.Count(); i++)
            {
                DiskiNode temp_node = GetiNode(fold_node.next_addr[i]);
                if (temp_node.name == name && temp_node.type == type)
                    return true;
            }
            return false;
        }
        /// <summary>
        /// 创建文件(夹)：分配i节点, 创建文件者拥有文件的全部权限！！
        /// </summary>
        /// <param name="type"></param>
        /// <param name="fname"></param>
        /// <returns></returns>
        public DiskiNode Create(ItemType type, string fname)
        {
            uint curfolder = sys_current_user.current_folder;

            DiskiNode fold_node = GetiNode(curfolder);
            //1,支持在指定的文件路径下创建文件(夹). [revise by Lau Xueyuan, 2019-06-24 01:33]
            //此处为相对路径
            if (fname.Contains("/"))
            {
                string[] filepath = fname.Split("/");
                string newpath = "";
                for (int i = 0; i < filepath.Length - 1; newpath += (filepath[i]+"/"), i++) ;
                fname = filepath[filepath.Count() - 1];
                //Console.WriteLine(newpath);
                List<DiskiNode> fold_node_tmp = GetiNodeByPath(newpath);
                //Console.WriteLine(fold_node_tmp.Count());
                if (fold_node_tmp.Count > 1) { return new DiskiNode(0, ".", 0); }
                else
                {
                    fold_node = fold_node_tmp[0];
                }
                if (fold_node.name == ".") return fold_node; 
            }
            if (IsNameConflict(fold_node, fname, type)) //出现同名冲突
            {
                Console.WriteLine("Name Conflict!");
                return new DiskiNode(0, ".", 0);
            }
            //2,分配i节点,分配磁盘块,上级i节点更新,写回磁盘
            uint id = AllocAiNodeID();
            DiskiNode ndn;
            Dictionary<uint, uint> author = new Dictionary<uint, uint>();
            author.Add(sys_current_user.uid, 7);
            if (sys_current_user.uid != 0) author.Add(0, 7);
            if (type == ItemType.FOLDER)
            {
                ndn = new DiskiNode(id, fname, 0, author)
                {
                    type = ItemType.FOLDER
                };
            }
            else
            {
                uint block_addr = AllocADiskBlock();
                ndn = new DiskiNode(id, fname, 1, author)
                {
                    type = ItemType.FILE
                };
                ndn.next_addr.Add(block_addr);
            }
            ndn.fore_addr = fold_node.id;
            fold_node.next_addr.Add(id);
            ndn.t_create = DateTime.Now;
            ndn.t_revise = DateTime.Now;
            if (sys_inode_tt.tt[id % 128] == null)
                sys_inode_tt.tt[id % 128] = new iNodeTable();
            sys_inode_tt.tt[id % 128].di_table.Add(ndn);
            UpdateDiskSFi(false, true);
            return ndn;
        }
        /// <summary>
        /// 分配磁盘块,正常则返回块地址,错误则返回0,未写回超级块
        /// </summary>
        /// <returns></returns>
        public uint AllocADiskBlock()
        {
            uint block_addr = 0;
            //若最后一组块数大于1
            if (sys_sb.last_group_block_num > 1)
            {
                block_addr = sys_sb.last_group_addr[0];
                sys_sb.last_group_block_num--;
                sys_sb.last_group_addr.RemoveAt(0);
            }
            //若最后一组块数=1，使用组长块，并把倒数第二组加到超级块
            else
            {
                block_addr = sys_sb.last_group_addr[0];
                FileStream fs_alloc = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
                BinaryFormatter bin_alloc = new BinaryFormatter();
                fs_alloc.Position = block_addr;
                BlockLeader bl_alloc = (BlockLeader)bin_alloc.Deserialize(fs_alloc);
                fs_alloc.Close();
                sys_sb.last_group_addr.RemoveAt(0);
                sys_sb.last_group_addr = bl_alloc.block_addr;
                sys_sb.last_group_block_num = SuperBlock.BLOCK_IN_GROUP;
            }
            return block_addr;
        }
        /// <summary>
        /// 首次安装文件系统，分配超级块、用户、i节点、组长块，返回true(成功)或false(失败)
        /// </summary>
        /// <returns></returns>
        public bool Install()
        {
            User root = new User(0,"");
            User user1 = new User(1001, "123");
            User user2 = new User(1002, "123");
            User user3 = new User(2001, "abc123");
            List<User> ut = new List<User>
            {
                root,
                user1,
                user2,
                user3
            };
            FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            BinaryFormatter binFormat = new BinaryFormatter();
            fs.Position = SuperBlock.USER_DISK_START * SuperBlock.BLOCK_SIZE;//用户信息，1~9块，9KB
            binFormat.Serialize(fs, ut);
            fs.Close();
            //设置超级管理员和普通用户
            Console.WriteLine("Install File System");
            Console.WriteLine("We need super administrator to log in to finish installation.");
            /*
            if (!LoginSys()) {
                if (sys_current_user.uid != 0)
                {
                    Console.WriteLine("Your permissions are insufficient to install the file system!");
                    LogoutSys();
                    return false;
                }
            }
            */
            Format();     //格式化
            //LogoutSys();  //超级管理员用户退出
            return true;
        }
        /// <summary>
        /// 格式化文件系统，自动检测用户并根据级别格式化不同大小的区域，返回true(成功)false(失败)
        /// </summary>
        /// <returns></returns>
        public bool Format()
        {
            FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            BinaryFormatter binFormat = new BinaryFormatter();
            Dictionary<uint, uint> author1 = new Dictionary<uint, uint>();
            author1.Add(0, 7); //uid零号超级管理员对root具有全部权限
            author1.Add(1001, 7);
            author1.Add(1002, 7);
            author1.Add(2001, 7);
            //若是超级管理员格式化磁盘
            //重置超级块
            SuperBlock sb = new SuperBlock();
            //创建root文件夹
            DiskiNode root_inode = new DiskiNode(0, "root", 0, author1) { fore_addr = 0, type = ItemType.FOLDER, t_create = DateTime.Now, t_revise = DateTime.Now };
            //创建回收站
            DiskiNode recycle_inode = new DiskiNode(1, "recyclebin", 0, author1) { fore_addr = 0,type=ItemType.FOLDER, t_create = DateTime.Now, t_revise = DateTime.Now };
            Dictionary<uint, uint> recyclebinMap = new Dictionary<uint, uint>();
            //把root和回收站添加到i节点列表里
            iNodeTT ins_tt = new iNodeTT();
            ins_tt.tt[0] = new iNodeTable();
            ins_tt.tt[0].di_table.Add(root_inode);
            ins_tt.tt[1] = new iNodeTable();
            ins_tt.tt[1].di_table.Add(recycle_inode);
            root_inode.next_addr.Add(recycle_inode.id);

            //初始化用户文件夹
            Dictionary<uint, uint> author2 = new Dictionary<uint, uint>();
            author2.Add(1001, 7); author2.Add(0, 7);
            DiskiNode usr1 = new DiskiNode(2, "usr1001", 0, author2) { type = ItemType.FOLDER, t_create = DateTime.Now, t_revise = DateTime.Now };
            ins_tt.tt[2] = new iNodeTable();
            ins_tt.tt[2].di_table.Add(usr1);

            Dictionary<uint, uint> author3 = new Dictionary<uint, uint>();
            author3.Add(1002, 7); author3.Add(0, 7);
            DiskiNode usr2 = new DiskiNode(3, "usr1002", 0, author3) { type = ItemType.FOLDER, t_create = DateTime.Now, t_revise = DateTime.Now };
            ins_tt.tt[3] = new iNodeTable();
            ins_tt.tt[3].di_table.Add(usr2);

            Dictionary<uint, uint> author4 = new Dictionary<uint, uint>();
            author4.Add(2001, 7); author4.Add(0, 7);
            DiskiNode usr3 = new DiskiNode(4, "usr2001", 0, author4) { type = ItemType.FOLDER, t_create = DateTime.Now, t_revise = DateTime.Now };
            ins_tt.tt[4] = new iNodeTable();
            ins_tt.tt[4].di_table.Add(usr3);
            root_inode.next_addr.Add(usr1.id);
            root_inode.next_addr.Add(usr2.id);
            root_inode.next_addr.Add(usr3.id);
            
            //重置超级栈
            sb.last_group_addr = new List<uint>();
            for (uint i = 0; i < SuperBlock.BLOCK_IN_GROUP; i++) { sb.last_group_addr.Add((4000+i)* SuperBlock.BLOCK_SIZE); }
            //组长块格式化，这里的32仅仅是前期为了快速建系统，之后要改成数据区组数，即4092*1024/128=32736
            for (uint i = 0; i < 32736; i++)
            {
                BlockLeader bl = new BlockLeader
                {
                    next_blocks_num = SuperBlock.BLOCK_IN_GROUP
                };
                for (uint j = 0; j < 128; j++)
                {
                    bl.block_addr.Add((4000 + (i+1) * SuperBlock.BLOCK_IN_GROUP + j)*SuperBlock.BLOCK_SIZE);
                }
                fs.Position = (4000 + i * SuperBlock.BLOCK_IN_GROUP + 127) * SuperBlock.BLOCK_SIZE;
                binFormat.Serialize(fs, bl);
            }
            //超级块区写磁盘，占0~2号块
            fs.Position = SuperBlock.SB_DISK_START * SuperBlock.BLOCK_SIZE;
            binFormat.Serialize(fs, sb);
            //回收站空map表写磁盘，占10~99号块
            fs.Position = SuperBlock.RECYCLEBINMAP_DISK_START * SuperBlock.BLOCK_SIZE;
            binFormat.Serialize(fs, recyclebinMap);
            //i节点区写磁盘，占100~3999号块
            fs.Position = SuperBlock.iNODE_DISK_START * SuperBlock.BLOCK_SIZE;
            binFormat.Serialize(fs, ins_tt);
            fs.Close();
            return true;
        }
        /// <summary>
        /// 清除一个磁盘块，写满\0
        /// </summary>
        /// <param name="block_order"></param>
        /// <returns></returns>
        public bool EraseBlock(uint block_order)
        {
            string str = "";
            for (int i = 0; i < 1024; str += 'd', i++) ;
            FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            fs.Seek(block_order * 1024, SeekOrigin.Begin);
            byte[] byteArray = Encoding.Default.GetBytes(str);
            try
            {
                fs.Write(byteArray, 0, byteArray.Length);
                fs.Close();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
            return true;
        }
        /// <summary>
        /// 回收磁盘块
        /// </summary>
        /// <param name="block_addr"></param>
        /// <returns></returns>
        public bool RecycleDiskBlock(uint block_addr)
        { 
            EraseBlock(block_addr);
            //若最后一组未满
            if (sys_sb.last_group_block_num < SuperBlock.BLOCK_IN_GROUP)
            {
                sys_sb.last_group_addr.Insert(0, block_addr);
                sys_sb.last_group_block_num++;
            }
            //最后一组满了，新增一组
            else
            {
                BlockLeader newBL = new BlockLeader
                {
                    next_blocks_num = SuperBlock.BLOCK_IN_GROUP,
                    block_addr = sys_sb.last_group_addr
                };
                sys_sb.last_group_block_num = 1;
                sys_sb.last_group_addr.Clear();
                sys_sb.last_group_addr.Add(block_addr);
                //写回组长块
                FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
                BinaryFormatter binFormat = new BinaryFormatter();
                fs.Position = block_addr;
                binFormat.Serialize(fs, newBL);
                fs.Close();
            }
            //写回超级块
            UpdateDiskSFi(true, false);
            return true;
        }
        /// <summary>
        /// 通过i节点ID来回收文件(夹)的i节点，删除文件（删除文件夹需要级联，不在这里）
        /// </summary>
        /// <param name="iNodeId"></param>
        /// <returns></returns>
        public bool RecycleiNode(uint iNodeId)
        {
            uint temp_id = iNodeId % SuperBlock.BLOCK_IN_GROUP;
            for (int i = 0; i < sys_inode_tt.tt[temp_id].di_table.Count(); i++)
            {
                if (sys_inode_tt.tt[temp_id].di_table[i].id == iNodeId)
                {
                    DiskiNode rdn = sys_inode_tt.tt[temp_id].di_table[i];
                    //如果是文件夹
                    if (rdn.type == ItemType.FOLDER) { }
                    //如果是文件，要回收所有磁盘块
                    else if(rdn.type==ItemType.FILE)
                    {
                        for (int j = 0; j < rdn.next_addr.Count(); j++)
                        {
                            Console.WriteLine("Recycle a Disk Block.");
                            RecycleDiskBlock(rdn.next_addr[j]);
                        }
                    }
                    sys_inode_tt.tt[temp_id].di_table.RemoveAt(i);
                    //把其上级i节点中的next_addr中的它的ID删掉
                    DiskiNode fore_dn = GetiNode(rdn.fore_addr);
                    fore_dn.next_addr.Remove(iNodeId);
                    break;
                }
            }
            UpdateDiskSFi(false, true);
            return true;
        }

        /// <summary>
        /// 通过路径删除文件
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool DeleteFile(string path)
        {
            DiskiNode temp_dn = GetiNodeByPath(path)[0];
            if (temp_dn.name == ".") { Console.WriteLine("No Such File."); return false; }
            if(new uint[]{ 2,3,6,7 }.Contains(temp_dn.uid[sys_current_user.uid]))
            {
                //当前用户有w权限，可以删除文件
                Console.WriteLine(temp_dn.id);
                if (temp_dn.type == ItemType.FOLDER) {
                    //该函数不能删除文件夹，需要调用专门的函数DeleteFolder
                    Console.WriteLine("This is a folder!");
                }
                else
                {
                    //是文件，可以删除
                    RecycleiNode(temp_dn.id);
                    Console.WriteLine("Successfully to Delete File.");
                    //OutputTT();
                }
                return true;
            }
            else
            {
                //当前用户没有写权限，不能删除文件
                Console.WriteLine("Have no sufficient permissions to delete this file!");
                return false;
            }
        }

        /// <summary>
        /// 递归删除一个文件夹
        /// </summary>
        /// <param name="inode"></param>
        private void DeleteAFolder(DiskiNode inode)
        {
            if (inode.type == ItemType.FOLDER)
            {
                List<DiskiNode> delList = new List<DiskiNode>();
                delList = (from item in inode.next_addr
                           select GetiNode(item)).ToList();
                foreach (DiskiNode item in delList)
                {
                    DeleteAFolder(item);
                }
                //删除自身（当前文件夹）
                RecycleiNode(inode.id);
            }
            else
            {
                //type == ItemType.FILE
                RecycleiNode(inode.id);
            }
        }
        /// <summary>
        /// 删除文件夹
        /// </summary>
        /// <param name="path">文件夹路径</param>
        public void DeleteFolder(string path)
        {
            List<DiskiNode> dellist = GetiNodeByPath(path);
            foreach (DiskiNode item in dellist)
            {
                DeleteAFolder(item);
            }
        }
        /// <summary>
        /// 复制一个文件夹
        /// </summary>
        /// <param name="src">原文件(夹)i结点</param>
        /// <param name="tar">目的路径</param>
        private void CopyAFolder(DiskiNode src, string tar)
        {
            string newName = tar + "/" + src.name;
            if (src.type == ItemType.FOLDER)
            {
                //文件夹
                List<DiskiNode> itemlist = new List<DiskiNode>();
                itemlist = (from itemid in src.next_addr
                            select GetiNode(itemid)).ToList();
                DiskiNode newfolder = Create(ItemType.FOLDER, newName);
                if (newfolder.name != ".") //成功创建文件夹
                {
                    foreach (DiskiNode item in itemlist)
                    {
                        CopyAFolder(item, tar + "/" + newfolder);
                    }
                }
            }
            else
            {
                //文件
                CopyFile(newName, tar);
            }
        }
        /// <summary>
        /// 复制文件夹
        /// </summary>
        /// <param name="fname">源文件名</param>
        /// <param name="tarpath">目的目录</param>
        public void CopyFolder(string fname, string tarpath)
        {
            List<DiskiNode> fromlist = GetiNodeByPath(fname);
            foreach (DiskiNode folder in fromlist)
            {
                CopyAFolder(folder, tarpath);
            }
        }
        /// <summary>
        /// 通过路径读取文件
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public string ReadFile(string path)
        {
            DiskiNode read_dn = GetiNodeByPath(path)[0];
            if(sys_current_user.open_file.Contains(read_dn.id))
            {
                Console.WriteLine("Someone has open " + read_dn.name);
                return "";
            }
            if (!new uint[] { 4,5,6,7 }.Contains(read_dn.uid[sys_current_user.uid])){
                Console.WriteLine("You do not have sufficient permissions to read " + read_dn.name);
                return "";
            }
            if (read_dn.name == ".") { Console.WriteLine("No Such File.");return ""; }
            string file_content = "";
            if (read_dn.type != ItemType.FILE) { return "This is a folder!"; }
            else
            {
                FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
                for (int i=0;i<read_dn.next_addr.Count;i++)
                {
                    byte[] byData = new byte[1024];
                    fs.Position = read_dn.next_addr[i];
                    fs.Read(byData, 0, byData.Length);
                    file_content += System.Text.Encoding.Default.GetString(byData);
                }
                fs.Close();
                sys_current_user.open_file.Remove(read_dn.id);
                return file_content;
            }
        }
        /// <summary>
        /// 通过路径写文件
        /// </summary>
        /// <param name="path"></param>
        /// <param name="file_content"></param>
        /// <returns></returns>
        public bool WriteFile(string path)
        {
            DiskiNode wdn = GetiNodeByPath(path)[0];
            if (!new uint[] { 2, 3, 6, 7 }.Contains(wdn.uid[sys_current_user.uid]))
            {
                Console.WriteLine("You do not have sufficient permissions to write in" + wdn.name);
                return false;
            }
            if (sys_current_user.open_file.Contains(wdn.id))
            {
                Console.WriteLine("Someone has open " + wdn.name);
                return false;
            }
            Console.WriteLine("Please input:");
            string file_content = Console.ReadLine();
            if (wdn.name == ".") { Console.WriteLine("No Such File.");return false; }
            int len = (int) wdn.next_addr.Count;
            //截取字符串
            int num = (file_content.Length / (int)SuperBlock.BLOCK_SIZE) + 1;

            //Console.WriteLine("num:" + num.ToString());
            //Console.WriteLine("len:" + len.ToString());

            Console.WriteLine("addr"+wdn.next_addr[0]);
            //若写入字节大于原有磁盘块，分配新盘快
            if (num > len) { for (int i = 0; i < num - len;wdn.next_addr.Add(AllocADiskBlock()), i++) ; }
            //若写入字节小于原有磁盘块，回收旧盘块
            else if (num < len)
            {
                for (int i = 0; i < len - num; i++)
                {
                    int addr_len = wdn.next_addr.Count();
                    RecycleDiskBlock(wdn.next_addr[addr_len - 1]);
                    wdn.next_addr.RemoveAt(addr_len - 1);
                }
            }
            //for (int i = 0; i < num; i++)
            //{
            //    //RecycleDiskBlock(wdn.next_addr[i]);
            //    //wdn.next_addr[i] = AllocADiskBlock();
            //    EraseBlock(wdn.next_addr[i]);
            //}
            //逐块写入
            //for (int i = 0; i < num; i++)
            //    EraseBlock(wdn.next_addr[i]);
            string str = "";
            for (int i = 0; i < 1024; str += '\0', i++);
            FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            for (int i = 0; i < num; i++)
            {
                //Console.WriteLine("wdn.next_addr[i]:" + wdn.next_addr[i]);
                int leng = (file_content.Length - i * 1024 > 1024) ? 1024 : file_content.Length - i * 1024;
                string file_block_temp = file_content.Substring(i * 1024, leng);
                byte[] byte_block = System.Text.Encoding.Default.GetBytes(file_block_temp);
                byte[] byk = System.Text.Encoding.Default.GetBytes(str);
                //Console.WriteLine(System.Text.Encoding.Default.GetString(byte_block));
                fs.Position = wdn.next_addr[i];
                fs.Write(byk, 0, byk.Length);
                fs.Position = wdn.next_addr[i];
                fs.Write(byte_block, 0, byte_block.Length);
            }
            fs.Close();
            wdn.size =(uint) ((num > len) ? num : len);
            sys_current_user.open_file.Remove(wdn.id);
            //更新i节点，超级块，文件目录
            UpdateDiskSFi();
            return true;
        }

        /// <summary>
        /// 通过路径重命名文件(夹)
        /// </summary>
        /// <param name="path"></param>
        /// <param name="name"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool Rename(string path, string name,ItemType type)
        {
            DiskiNode rdn = GetiNodeByPath(path)[0];
            if(rdn.name == ".") { Console.WriteLine("No Such File/Folder.");return false; }
            if (IsNameConflict(rdn, name, type)) { return false; }
            else
            {
                rdn.name = name;
                return true;
            }
        }
        /// <summary>
        /// 显示某路径下的文件
        /// </summary>
        /// <param name="path"></param>
        public void ShowFile(string path)
        {
            DiskiNode dn = GetiNodeByPath(path)[0];
            if (dn.name == ".") { Console.WriteLine("No Such Path."); }
            for(int i=0;i<dn.next_addr.Count();i++)
            {
                DiskiNode dn_temp = GetiNode(dn.next_addr[i]);
                Console.WriteLine(i + ":|" + dn_temp.name + "|(ID)" + dn_temp.id + "|"+dn_temp.type);
            }
        }
        /// <summary>
        /// 复制旧i节点物理盘块到新i节点物理盘块，正常返回true
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public bool CopyiNodeDisk(DiskiNode from, DiskiNode to)
        {
            if (from.next_addr.Count() != to.next_addr.Count()) { return false; }//Console.WriteLine("From's block != To's block"); return false; }
            else
            {
                int block_num = from.next_addr.Count();
                FileStream fs = new FileStream("filesystem", FileMode.OpenOrCreate, FileAccess.ReadWrite);
                //一块一块地读，因为可能不连续
                for (int i = 0; i < block_num; i++)
                {
                    uint b_from = from.next_addr[i] * SuperBlock.BLOCK_SIZE;
                    uint b_to = to.next_addr[i] * SuperBlock.BLOCK_SIZE;
                    Byte[] b_content = new byte[SuperBlock.BLOCK_SIZE];
                    fs.Position = b_from;
                    fs.Read(b_content, 0, (int)SuperBlock.BLOCK_SIZE);
                    fs.Position = b_to;
                    fs.Write(b_content, 0, b_content.Length);
                }
                fs.Close();
                return true;
            }
        }
        public void testReg()
        {
            string[] bs = { "c.txt","pc.txt","cvtxt"};
            for(int i=0;i<bs.Length;i++)
            {
                if (MatchString(bs[i],"c.t~"))
                {
                    Console.WriteLine(bs[i]);
                }
            }

        }
        public void InitializationForTest()
        {
            Create(ItemType.FILE, "log.txt");
            Create(ItemType.FOLDER, "usr1001/Software");
            Create(ItemType.FILE, "usr1001/main.cpp");
            Create(ItemType.FILE, "usr1001/Software/ss.txt");
            Create(ItemType.FILE, "usr1002/1.cpp");
            Create(ItemType.FILE, "usr2001/2.cpp");
            Create(ItemType.FILE, "usr2001/main.cpp");
            FileStream fs = new FileStream("install.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            fs.Close();
        }
        /// <summary>
        /// 复制一个文件到另一个目录下（不支持复制文件夹！）
        /// </summary>
        /// <param name="filename">源文件名(或带路径的文件名)，不能是一个文件夹！</param>
        /// <param name="tarpath">目的路径</param>
        public bool CopyFile(string filename, string tarpath)
        {
            List<DiskiNode> from = GetiNodeByPath(filename);
            foreach (DiskiNode file in from)
            {
                //判断当前用户对源文件是否有移动权限
                if (!new uint[] { 4, 5, 6, 7 }.Contains(file.uid[sys_current_user.uid]))
                {
                    from.Remove(file); //将没有移动权限的文件从移动文件列表中移除
                    Console.WriteLine("You have no permissions to copy " + file.name);
                }
            }
            if (from.Count() == 0) return false; //没有可移动的文件
            DiskiNode to = GetiNodeByPath(tarpath).First();
            if(!new uint[] { 2,3,6,7 }.Contains(to.uid[sys_current_user.uid]))
            {
                //没有足够的权限修改目的文件夹
                Console.WriteLine("You do not have sufficient permissions to copy files to " + to.name);
                return false;
            }
            foreach (DiskiNode inode in from)
            {
                bool collision = false;
                //冲突检查
                foreach (uint id in to.next_addr)
                {
                    //发生同名同类型冲突
                    if (inode.name == GetiNode(id).name &&
                        inode.type == GetiNode(id).type)
                    {
                        collision = true;
                        Console.WriteLine("cannot overwrite directory '" + tarpath + "/" + inode.name + "' with non-directory");
                        break;
                    }
                }
                if (collision == true) continue;
                if (inode.type == ItemType.FOLDER) return false; //排除文件夹
                DiskiNode newiNode = new DiskiNode(AllocAiNodeID(), inode.name, inode.size, inode.uid)
                {
                    fore_addr = to.id
                };
                for (int i = 0; i < inode.next_addr.Count; i++)
                {
                    newiNode.next_addr.Add(AllocADiskBlock());
                }
                newiNode.t_create = DateTime.Now;
                newiNode.t_revise = DateTime.Now;
                if (sys_inode_tt.tt[newiNode.id % 128] == null)
                    sys_inode_tt.tt[newiNode.id % 128] = new iNodeTable();
                sys_inode_tt.tt[newiNode.id % 128].di_table.Add(newiNode);
                to.next_addr.Add(newiNode.id);
                CopyiNodeDisk(inode, newiNode);
            }
            UpdateDiskSFi(true, true);
            return true;
        }
        /// <summary>
        /// 移动一个文件filename或文件夹到另一个目录下tarpath()
        /// </summary>
        /// <param name="filename">文件(夹)名(当前目录下的文件)或带相对路径的文件</param>
        /// <param name="tarpath">移动到的目的地址</param>
        public bool Move(string filename, string tarpath)
        {
            //若为模糊输入，返回所有匹配结果的i结点
            List<DiskiNode> fromlist = GetiNodeByPath(filename);
            foreach (DiskiNode file in fromlist)
            {
                //判断当前用户对源文件是否有移动权限
                if (!new uint[] { 2, 3, 6, 7 }.Contains(file.uid[sys_current_user.uid]))
                {
                    fromlist.Remove(file); //将没有移动权限的文件从移动文件列表中移除
                    Console.WriteLine("You have no permissions to move " + file.name);
                }
                if (file.id == 1) fromlist.Remove(file);
            }
            if (fromlist.Count() == 0) return false; //没有可移动的文件
            DiskiNode to = GetiNodeByPath(tarpath).First();
            if (!new uint[] { 2, 3, 6, 7 }.Contains(to.uid[sys_current_user.uid]))
            {
                //没有足够的权限修改目的文件夹
                Console.WriteLine("You do not have sufficient permissions to move files to " + to.name);
                return false;
            }
            foreach (DiskiNode inode in fromlist)
            {
                //把原地址上一级（父级）i结点中存的下一级信息中有关该节点的id删除
                GetiNode(inode.fore_addr).next_addr.Remove(inode.id);
                //再检查目标文件夹中是否有同名同类型文件冲突，有则直接覆盖
                IEnumerable<uint> collision = from id in to.next_addr
                                              where GetiNode(id).name == inode.name &&
                                                    GetiNode(id).type == inode.type
                                              select id;
                //每次最多只有一个文件(夹)出现冲突，解决冲突的办法是直接覆盖
                if (collision.Count() > 0)
                {
                    to.next_addr.Remove(collision.First());
                }
                inode.fore_addr = to.id;  //修改该文件(夹)父级指针
                to.next_addr.Add(inode.id);
            }
            UpdateDiskSFi(false, true); //将变更写回磁盘
            return true;
        }
        /// <summary>
        /// 进入某一文件夹
        /// </summary>
        /// <param name="foldername"></param>
        public bool ChangeCurrentDirectory(string foldername)
        {
            List<DiskiNode> inode = GetiNodeByPath(foldername);
            if (inode.Count() > 1)
            {
                if(!(inode.Count()==2 && inode[0].name == inode[1].name))
                {
                    Console.WriteLine("cd: too many arguments");
                    return false;
                }
            }
            if (inode[0].type == ItemType.FILE)
            {
                Console.WriteLine("This is a FILE.");
                return false;
            }
            else
            {
                if(!new uint[] { 1,3,5,7 }.Contains(inode.First().uid[sys_current_user.uid]))
                {
                    Console.WriteLine("You do not have sufficient permissions to step in this folder!");
                    return false;
                }
                sys_current_user.current_folder = inode.First().id;
            }
            return true;
        }
        /// <summary>
        /// 将一个文件移入回收站
        /// </summary>
        /// <param name="path">文件的路径</param>
        public bool MoveToRecycleBin(string path)
        {
            List<DiskiNode> delitem = GetiNodeByPath(path);
            foreach(DiskiNode file in delitem)
            {
                if (!new uint[] { 2, 3, 6, 7 }.Contains(file.uid[sys_current_user.uid]))
                {
                    delitem.Remove(file);
                    Console.WriteLine("You do not have permissions to delete " + file.name);
                    return false;
                }
            }
            if (delitem.Count() == 0) return false;
            DiskiNode recyclebin = GetiNode(1);   //获取回收站i结点
            foreach (DiskiNode item in delitem)
            {
                recyclebinMap.Add(item.id, item.fore_addr);
                GetiNode(item.fore_addr).next_addr.Remove(item.id);
                item.fore_addr = 1;
                recyclebin.next_addr.Add(item.id);
            }
            return true;
        }

        /// <summary>
        /// 恢复回收站中的文件或文件夹
        /// </summary>
        /// <param name="name">文件名</param>
        /// <returns>返回还原文件的i结点</returns>
        public DiskiNode RestoreFromRecycleBin(string name)
        {
            DiskiNode recyclebin = GetiNode(1);
            List<uint> restore = (from item in recyclebin.next_addr
                                  where GetiNode(item).name == name
                                  select item).ToList();
            uint removeid = restore.First();
            if (restore.Count() > 1)
            {
                for (int i = 0; i < restore.Count(); i++)
                {
                    DiskiNode inode = GetiNode(restore[i]);
                    if (!new uint[] { 2, 3, 6, 7 }.Contains(inode.uid[sys_current_user.uid]))
                    {
                        restore.Remove(inode.id);
                        continue;
                    }
                    Console.WriteLine("id: " + inode.id + ", name: " + inode.name + ", type: " + inode.type +
                        ", size: " + inode.size + ", revise time: " + inode.t_revise);
                }
                if (restore.Count() == 0)
                {
                    Console.WriteLine("File or Directory does not exists!");
                    return new DiskiNode(0, ".", 0); //没有可以还原的文件
                }
                Console.Write("Please select a file or folder to restore: ");
                uint id = Convert.ToUInt32(Console.ReadLine());
                IEnumerable<uint> tmpid = from c in restore
                                          where c == id
                                          select c;
                //用户输入的文件不存在
                if (tmpid.Count() == 0)
                {
                    Console.WriteLine("File or Directory does not exists!");
                    return new DiskiNode(0, ".", 0);
                }
                removeid = id;
            }
            DiskiNode node = GetiNode(removeid);
            node.fore_addr = recyclebinMap[removeid];
            GetiNode(node.fore_addr).next_addr.Add(removeid);
            GetiNode(1).next_addr.Remove(removeid);
            return node;
        }

        /// <summary>
        /// 显示回收站内容
        /// </summary>
        public void ShowRecycleBin()
        {
            DiskiNode recyclebin = GetiNode(1);
            foreach (uint id in recyclebin.next_addr)
            {
                DiskiNode inode = GetiNode(id);
                if (new uint[] { 2, 3, 6, 7 }.Contains(inode.uid[sys_current_user.uid]))
                    Console.WriteLine("name: " + inode.name + ", type: " + inode.type +
                        ", size: " + inode.size + ", revise time: " + inode.t_revise);
            }
        }

        /// <summary>
        /// 清空回收站
        /// </summary>
        public void ClearRecycleBin()
        {
            DiskiNode recyclebin = GetiNode(1);
            List<uint> tmp = new List<uint>();
            foreach (uint item in recyclebin.next_addr)
            {
                tmp.Add(item);
            }
            foreach (uint item in tmp)
            {
                string path = "/recyclebin/";
                DiskiNode inode = GetiNode(item);
                if (new uint[] { 2, 3, 6, 7 }.Contains(inode.uid[sys_current_user.uid]))
                {
                    path += inode.name;
                    DeleteFolder(path);
                }
            }
        }

        /// <summary>
        /// 为搜索建立索引
        /// </summary>
        /// <param name="index"></param>
        public bool CreateIndexForSearch(string indexKey)
        {
            //Console.Write("Please enter an index-key to create an index: ");
            //string indexKey = Console.ReadLine();
            try
            {
                DatabaseOp dbHandler = new DatabaseOp();
                bool flag = dbHandler.CreateIndex(indexKey);
                List<DiskiNode> biglist = new List<DiskiNode>();
                foreach(iNodeTable item in sys_inode_tt.tt)
                {
                    if (item != null)
                    {
                        foreach (DiskiNode dn in item.di_table)
                        {
                            //if(dn!=null)
                            biglist.Add(dn);
                        }
                    }
                }
                dbHandler.LoadDataToDb(biglist);
                if (flag)
                    isCreateIndex[indexKey] = true;
                return flag;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }

        public void ExeSql()
        {
            DatabaseOp dbop = new DatabaseOp();
            List<DiskiNode> biglist = new List<DiskiNode>();
            foreach (iNodeTable item in sys_inode_tt.tt)
            {
                if (item != null)
                {
                    foreach (DiskiNode dn in item.di_table)
                    {
                        biglist.Add(dn);
                    }
                }
            }
            try
            {
                dbop.LoadDataToDb(biglist);
                while (true)
                {
                    Console.Write(">sql: ");
                    string sql = Console.ReadLine();
                    if (sql == "exit") return;
                    dbop.ExecuteUserCmd(sql);
                }
            }
            catch
            {
                return;
            }
           
        }

        /// <summary>
        /// 全盘搜索一个文件（支持模糊查找）
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>返回所有同名文件的i节点</returns>
        public List<DiskiNode> SearchInAllDisk(string filename)
        {
            List<DiskiNode> reslist = new List<DiskiNode>();
            //未建立索引，需要遍历全树   
            if (!isCreateIndex["name"])
            {
                Stack<DiskiNode> stack = new Stack<DiskiNode>();
                stack.Push(GetiNode(0));
                while (stack.Count() != 0)
                {
                    DiskiNode visit = stack.Peek();
                    stack.Pop();
                    if (MatchString(visit.name, filename))
                    {
                        //找到一个与filename名字相同的文件(夹)
                        reslist.Add(visit);
                    }
                    for (int i = visit.next_addr.Count() - 1; i >= 0; i--)
                    {
                        DiskiNode inode = GetiNode(visit.next_addr[i]);
                        if(new uint[] { 5,7 }.Contains(inode.uid[sys_current_user.uid]))
                            stack.Push(GetiNode(visit.next_addr[i]));
                    }
                }
            }
            else
            {
                //已建立索引，使用数据库进行查询
                DatabaseOp dbHandler = new DatabaseOp();
                reslist = dbHandler.SearchFileUsingDb(filename);
                foreach(DiskiNode item in reslist)
                {
                    //排除无权限访问的文件
                    if (!new uint[] { 5, 7 }.Contains(GetiNode(item.id).uid[sys_current_user.uid]))
                        reslist.Remove(item);
                }
            }
            
            if (reslist.Count == 0)
            {
                Console.WriteLine("No file or folder named " + filename + " was found!");
            }
            else
            {
                //输出
                Console.WriteLine("找到符合条件的项目个数：" + reslist.Count());
                foreach (DiskiNode item in reslist)
                {
                    Console.WriteLine(item.name);
                    if (item.type == ItemType.FOLDER)
                    {
                        DiskiNode curfolder = item;
                        if (isCreateIndex["name"])
                        {
                            curfolder = GetiNode(item.id);
                        }
                        foreach (uint subid in curfolder.next_addr)
                        {
                            Console.WriteLine(GetiNode(subid).name);
                        }
                    }
                }
            }
            return reslist;
        }

        /// <summary>
        /// 在当前目录下搜索（支持模糊查找）
        /// </summary>
        /// <param name="path">指定从那个目录开始搜索</param>
        /// <param name="filename">要搜索的文件名</param>
        /// <returns>返回当前目录下所有符合的文件i结点</returns>
        public List<DiskiNode> SearchFromSpecificFolder(string path, string filename)
        {
            List<DiskiNode> reslist = new List<DiskiNode>();
            Stack<DiskiNode> stack = new Stack<DiskiNode>();
            uint curfolder = sys_current_user.current_folder;
            if (path != "")
            {
                //从path指定的目录开始搜索
                string[] filepath = path.Split("/");
                foreach (string folder in filepath)
                {
                    //返回当前目录下名字为folder的文件(夹)
                    foreach (uint itemid in GetiNode(curfolder).next_addr)
                    {
                        if (GetiNode(itemid).name == folder)
                        {
                            curfolder = itemid; //改变当前目录（但不改变用户项中当前目录）
                            break;
                        }
                    }
                }
                //curfolder即为搜索的根目录
            }

            //从当前目录下开始搜索
            stack.Push(GetiNode(curfolder));
            while (stack.Count != 0)
            {
                DiskiNode visit = stack.Peek();
                stack.Pop();
                if (MatchString(visit.name, filename))
                {
                    reslist.Add(visit);
                    Console.WriteLine(visit.name);
                }
                for (int i = visit.next_addr.Count - 1; i >= 0; i--)
                {
                    stack.Push(GetiNode(visit.next_addr[i]));
                }
            }
            return reslist;
        }

        /// <summary>
        /// 计算文件（夹）大小
        /// </summary>
        /// <param name="inode">文件(夹)的i结点</param>
        /// <returns>返回文件(夹)大小</returns>
        public uint CalFileOrFolderSize(DiskiNode inode)
        {
            uint size = 0;
            if (inode.type == ItemType.FOLDER)
            {
                foreach (uint subid in inode.next_addr)
                {
                    size += CalFileOrFolderSize(GetiNode(subid));
                }
            }
            else
            {
                size = inode.size;
            }
            return size;
        }
        /// <summary>
        /// 比较两个文件/文件夹
        /// </summary>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <param name="inode"></param>
        /// <returns></returns>
        public bool ComparedThem(string path1, string path2, bool inode)
        {
            string str1 = ReadFile(path1);
            string str2 = ReadFile(path2);
            if (!inode)
            {
                if (str1 == str2) return true;
                else return false;
            }
            else
            {
                DiskiNode dn1 = GetiNodeByPath(path1)[0];
                DiskiNode dn2 = GetiNodeByPath(path2)[0];
                if (str1 == str2 && dn1.name == dn2.name && dn1.size == dn2.size && dn1.t_create == dn2.t_create && dn1.t_revise == dn2.t_revise && dn1.uid == dn2.uid) return true;
                else return false;
            }
        }
        /// <summary>
        /// 输出i节点详情
        /// </summary>
        /// <param name="path"></param>
        /// <param name="addr"></param>
        public void ShowDetail(string path, bool addr)
        {
            DiskiNode ndn = GetiNodeByPath(path)[0];
            Console.WriteLine("|Type\t\t|Size\t\t|Owner\t\t|ID\t\t|Name\t\t");
            Console.WriteLine("|---------------|---------------|---------------|---------------|---------------");
            Console.WriteLine("|" + ndn.type + "\t\t|" + ndn.size + " KB\t\t|" + ndn.uid.FirstOrDefault().Key + "\t\t|" + ndn.id + "\t\t|" + ndn.name + "\t\t");
            if (addr)
                for (int i = 0; i < ndn.next_addr.Count; i++)
                    Console.WriteLine(ndn.next_addr[i] + "|");
        }
        /// <summary>
        /// 展示当前文件夹的情况
        /// </summary>
        public void ShowDirectory(string path = ".", string order = "type", bool lite = true)
        {
            iNodeTable it = new iNodeTable();
            List<DiskiNode> diriNode = new List<DiskiNode>();
            if (path == ".")
            { diriNode.Add(GetiNode(sys_current_user.current_folder)); }
            else { diriNode = GetiNodeByPath(path); }
            foreach(DiskiNode dn in diriNode)
            {
                foreach (uint itemid in dn.next_addr)
                {
                    it.di_table.Add(GetiNode(itemid));
                }
            }
            if (lite) ShowiNodeListLite(it, order);
            else ShowiNodeList(it, order);
        }
        /// <summary>
        /// 展示当前文件夹的情况LITE版本
        /// </summary>
        /// <param name="it"></param>
        /// <param name="order"></param>
        public void ShowiNodeListLite(iNodeTable it, string order)
        {
            DiskiNode testdn = GetiNode(0);
           // Console.WriteLine(testdn.uid.Keys);
            if (it.di_table.Count == 0)
            {
                Console.WriteLine("There is no file/folder.");
            }
            else
            {
                for (int i = 0; it.di_table[i].type == ItemType.FOLDER && i < it.di_table.Count; i++)
                {
                    it.di_table[i].size = CalFileOrFolderSize(it.di_table[i]);
                }
                if (order == "name") it.di_table.Sort((a, b) => a.name.CompareTo(b.name));
                else if (order == "type") it.di_table.Sort((a, b) => a.type.CompareTo(b.type));
                else if (order == "size") it.di_table.Sort((a, b) => a.size.CompareTo(b.size));
                else if (order == "create") it.di_table.Sort((a, b) => a.t_create.CompareTo(b.t_create));
                else if (order == "revise") it.di_table.Sort((a, b) => a.t_revise.CompareTo(b.t_revise));
            }
            for (int i = 0; i < it.di_table.Count; i++)
            {
                DiskiNode ndn = it.di_table[i];
                Console.WriteLine(ndn.name);
            }
        }
        /// <summary>
        /// 展示当前文件夹的情况
        /// </summary>
        public void ShowiNodeList(iNodeTable it, string order)
        {
            DiskiNode diriNode = GetiNode(sys_current_user.current_folder);
            
            if(!new uint[] { 4,5,6,7 }.Contains(diriNode.uid[sys_current_user.uid]))
            {
                Console.WriteLine("You do not have sufficient permissions to view this folder!");
                return;
            }
            
            if (diriNode.next_addr.Count() == 0)
            {
                Console.WriteLine("There is no file/folder.");
            }
            else
            {
                for (int i = 0; i < it.di_table.Count && it.di_table[i].type == ItemType.FOLDER; i++)
                {
                    it.di_table[i].size = CalFileOrFolderSize(it.di_table[i]);
                }
                if (order == "name") it.di_table.Sort((a, b) => a.name.CompareTo(b.name));
                else if (order == "type") it.di_table.Sort((a, b) => a.type.CompareTo(b.type));
                else if (order == "size") it.di_table.Sort((a, b) => a.size.CompareTo(b.size));
                else if (order == "create") it.di_table.Sort((a, b) => a.t_create.CompareTo(b.t_create));
                else if (order == "revise") it.di_table.Sort((a, b) => a.t_revise.CompareTo(b.t_revise));
                Console.WriteLine("|Type\t\t|Size\t\t|Owner\t\t|ID\t\t|Name\t\t");
                Console.WriteLine("|---------------|---------------|---------------|---------------|---------------");
            }
            for (int i = 0; i < it.di_table.Count; i++)
            {
                DiskiNode ndn = it.di_table[i];
                uint ssize = ndn.size;
                string strsize;
                if (ssize >= 1024) { strsize = (ssize / 1024).ToString() + " MB"; }
                else { strsize = ssize.ToString() + " KB"; }
                Console.WriteLine("|" + ndn.type + "\t\t|" + strsize + "\t\t|" + ndn.uid.FirstOrDefault().Key + "\t\t|" + ndn.id + "\t\t|" + ndn.name + "\t\t");
            }
        }

        /// <summary>
        /// 获取可能的子级权限
        /// </summary>
        /// <param name="pareauth">父级权限值</param>
        /// <param name="self_subauth">自定义子级权限值</param>
        /// <returns>所有可能的子级权限值</returns>
        private bool GetSubAuthority(uint pareauth, uint self_subauth)
        {
            uint[] res = new uint[] { };
            switch (pareauth)
            {
                case 0: res = new uint[] { 0 }; break;
                case 1: res = new uint[] { 1 }; break;
                case 2: res = new uint[] { 2 }; break;
                case 3: res = new uint[] { 1, 2, 3 }; break;
                case 4: res = new uint[] { 4 }; break;
                case 5: res = new uint[] { 1, 4, 5 }; break;
                case 6: res = new uint[] { 2, 4, 6 }; break;
                case 7: res = new uint[] { 1, 2, 3, 4, 5, 6, 7 }; break;
            }
            if (res.Contains(self_subauth)) return true;
            return false;
        }

        /// <summary>
        /// 给目标用户赋权
        /// </summary>
        /// <param name="fname">操作文件的文件名</param>
        /// <param name="anousr">目标用户</param>
        /// <param name="authority">权限值</param>
        /// <returns>是否赋权限成功</returns>
        public bool AssignAuthority(string fname, uint anousr, uint authority)
        {
            List<DiskiNode> inodelist = GetiNodeByPath(fname);
            if (inodelist.Count() > 1)
            {
                //是否找到了多个符号要求的目标，若是，则报错
                Console.WriteLine("Too many arguments!");
                return false;
            }
            if (authority < 0 || authority > 7
                || !GetSubAuthority(inodelist.First().uid[sys_current_user.uid], authority))
            {
                //检查权限值是否合法
                Console.WriteLine("Illegal value of permission!");
                return false;
            }
            inodelist.First().uid.Add(anousr, authority);
            return true;
        }

        /// <summary>
        /// 回收另一个用户的权限对文件的(目前只限超级管理员回收其他用户的权限)
        /// </summary>
        /// <param name="fname">文件名</param>
        /// <param name="anousr">指定用户</param>
        /// <param name="newauth">新权限值</param>
        /// <returns></returns>
        public bool RecycleAuthority(string fname, uint anousr, uint newauth)
        {
            if (sys_current_user.uid != 0)
            {
                Console.WriteLine("You do not have sufficient permissions to recycle others' authorities!");
                return false;
            }
            List<DiskiNode> inode = GetiNodeByPath(fname);
            if (inode.Count() > 1)
            {
                //是否找到了多个符号要求的目标，若是，则报错
                Console.WriteLine("Too many arguments!");
                return false;
            }
            if(newauth < 0 || newauth > 7 || !GetSubAuthority(inode.First().uid[anousr], newauth))
            {
                //检查权限值是否合法
                Console.WriteLine("Illegal value of permission!");
                return false;
            }
            inode.First().uid[anousr] = newauth;
            return true;
        }

        /// <summary>
        /// 运行测试
        /// </summary>
        public void exeall()
        {
            bool isInstall = false;
            if (!File.Exists("filesystem"))
            {
                //安装文件系统，会创建root,回收站,usr1001,usr1002,usr2001.!!!仅在首次运行时需要!!!
                isInstall = Install();
                if (!isInstall)
                {
                    Console.WriteLine("File system installation failed");
                    return;
                }
            }
            bool isStart = Start();//启动文件系统
            if (!isStart) return;
            if(!File.Exists("install.lock"))
                InitializationForTest();//批处理，创建一些文件和文件夹!!!首次运行时需要，之后注释掉!!!

            //CopyFolder("usr2001", "usr1001");
            //ChangeCurrentDirectory("usr1001");
            //ShowDirectory();
            //CreateIndexForSearch();
            //SearchInAllDisk("usr2001");

            //Console.WriteLine(new uint[] { 2, 3, 6, 7 }.Contains<uint>(3));
            //Console.WriteLine("-----------------");
            //Console.WriteLine("root:");
            //ShowFile("/");
            //Console.WriteLine("-----------------");
            //Console.WriteLine("root/usr1001:");
            //ShowFile("/usr1001");
            //Console.WriteLine("-----------------");
            //Console.WriteLine("root/usr1001/Software:");
            //ShowFile("/usr1001/Software");
            //Console.WriteLine("-----------------");
            //Console.WriteLine("root/usr1002:");
            //ShowFile("/usr1002");
            //Console.WriteLine("-----------------");
            //Console.WriteLine("root/usr2001:");
            //ShowFile("/usr2001");
            //Console.WriteLine("-----------------");
            //CreateIndexForSearch();
            //SearchInAllDisk("usr1001");
            //DirectOp op = new DirectOp();
            //op.ShowCurrentDirectory();
            //Console.WriteLine("curFolder: " + GetiNode(sys_current_user.current_folder).name);
            //Move("usr1001/Software/ss.txt", "usr1002");


            //CopyFile("usr2001/2.cpp", "usr1001/Software");
            //ChangeCurrentDirectory("usr1001");
            //ShowCurrentDirectory();


            //Console.WriteLine("CurFolder: " + GetiNode(sys_current_user.current_folder).name);
            //ForwardtoADirectory("../..");
            //Console.WriteLine("CurFolder: " + GetiNode(sys_current_user.current_folder).name);
            //ShowCurrentDirectory();
            //ForwardtoADirectory("usr1002");
            //Console.WriteLine("CurFolder: " + GetiNode(sys_current_user.current_folder).name);
            //ShowCurrentDirectory();

            //Console.WriteLine(list.Count());

            //DatabaseOp dbop = new DatabaseOp();

            //List<DiskiNode> list = new List<DiskiNode>();
            //for (int i = 1; i <= 5; i++)
            //{
            //    DiskiNode inode = new DiskiNode(Convert.ToUInt32(i), "test" + i.ToString() + ".txt", Convert.ToUInt32(1000 + i), Convert.ToUInt32(i + 1));
            //    list.Add(inode);
            //}
            //dbop.LoadDataToDb(list);
            //dbop.printHighscores();
            //string str = Console.ReadLine();

            //dbop.ExecuteUserCmd(str);
            //string sql = "create index index_{0} on InodeTab({0})";
            //sql = string.Format(sql, new string("id"));
            //Console.WriteLine(sql);
            //dbop.CreateIndex("id");
        }
    }
}
