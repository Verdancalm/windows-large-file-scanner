# Windows 本地大文件扫描器

一款免安装、完全本地运行的 Windows 大文件扫描与安全清理工具。

## 功能

- 自定义阈值扫描固定磁盘，默认 500 MB
- 按体积排序，显示路径、修改时间和清理建议
- 标注缓存、安装包、用户文件及系统/程序文件
- 打开所在位置、导出 CSV 清单
- 普通删除前二次确认
- 管理员强制删除，支持被占用文件重启后删除
- 保护 Windows、Program Files、ProgramData、分页/休眠文件及 MuMu 虚拟磁盘
- 完全离线，不收集或上传数据

## 获取与使用

目前仓库公开完整源代码。便携 EXE 将随后通过 GitHub Releases 提供；在此之前可按下方说明自行编译。

> 删除不会进入回收站，无法撤销，请先核对完整路径。

## 兼容性

推荐 Windows 10 / 11 64 位，依赖系统自带的 .NET Framework 4.x。

## 编译

使用 Windows 自带的 .NET Framework C# 编译器编译 `src/LargeFileScanner.cs`，并通过 `/win32icon:assets/app.ico` 嵌入你拥有授权的图标。

## 许可

代码采用 [MIT License](LICENSE)。项目图标不因代码许可证而获得独立再授权。
