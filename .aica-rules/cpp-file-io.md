---
paths:
  - "**/*.cpp"
  - "**/*.h"
  - "**/*.c"
  - "**/*.cc"
  - "**/*.cxx"
enabled: true
priority: 20
---

# C/C++ 文件读写规范 (Q/HNC 43)

## 文件操作配对
- fopen/fclose 必须配对，异常路径中不要忘记 fclose
- fclose 后将文件指针置 NULL
- nc_findfirst/nc_findclose 必须配对
- 禁止使用全局文件句柄变量

## 路径与打开
- 打开文件前用 stat() 检查路径有效性，不能仅靠 fopen 返回值
- Linux 下检查路径是否为文件夹（S_ISDIR）
- 文件名/文件夹名/路径名长度统一使用宏 PATH_NAME_LEN

## 读写检查
- fread/fwrite 必须检查返回值（实际读写的数据块个数）
- fseek 返回值不为 -1
- fgets 非文件尾时返回值不为空
- fputs 返回值不为 EOF

## 写文件落盘
- 写文件关闭前必须调用 fflush + fsync（Linux），保证数据立即写入磁盘
- 获取文件描述符：`Bit16 fd = fileno(fp);`

## 数据文件规范
- 新增数据文件必须有文件头（FileHead 结构）
- 保存时写入校验码（CRC32），载入时验证
- 新增文本文件必须有版本信息，读取时检查版本
