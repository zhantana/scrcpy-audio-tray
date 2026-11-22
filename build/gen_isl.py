# -*- coding: utf-8 -*-
import os

isl = """[LangOptions]
LanguageName=简体中文
LanguageID=$0804
LanguageCodePage=936
DialogFontName=Microsoft YaHei UI
DialogFontSize=9
WelcomeFontName=Microsoft YaHei UI
WelcomeFontSize=12
TitleFontName=Microsoft YaHei UI
TitleFontSize=29
CopyrightFontName=Microsoft YaHei UI
CopyrightFontSize=8

[Messages]
SetupAppTitle=安装
SetupWindowTitle=%1 安装程序
UninstallAppTitle=卸载
UninstallAppFullTitle=卸载 %1
SetupLdrStartupMessage=%1 即将安装到你的电脑上，继续吗？
ExitSetupTitle=退出安装
ExitSetupMessage=安装还没完成，现在退出就不会安装 %1。%n%n确定退出吗？
ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonOK=确定
ButtonCancel=取消
ButtonYes=是(&Y)
ButtonNo=否(&N)
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&R)...
ButtonWizardBrowse=浏览(&R)...
ClickNext=点「下一步」继续，或点「取消」退出。
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=这将在你的电脑上安装 [name/ver]。%n%n建议先关掉正在运行的其他程序，然后点「下一步」。
WizardSelectDir=选择安装位置
SelectDirDesc=要把 [name] 装到哪里？
SelectDirLabel3=安装程序会把 [name] 装到下面的文件夹。
SelectDirBrowseLabel=点「下一步」继续；要换位置就点「浏览」。
DiskSpaceMBLabel=至少需要 [mb] MB 可用空间。
WizardSelectTasks=选择附加任务
SelectTasksDesc=还要顺带做哪些事？
SelectTasksLabel2=安装 [name] 时，同时完成下面勾选的项目：
WizardReady=准备安装
ReadyLabel1=安装程序已准备好，现在开始安装 [name]。
ReadyLabel2a=点「安装」开始；想改设置就点「上一步」。
ReadyLabel2b=点「安装」开始。
WizardPreparing=准备安装
PreparingDesc=正在准备安装 [name]。
WizardInstalling=正在安装
InstallingLabel=正在安装 [name]，请稍候……
InstalledLabel=已装好：
WizardFinished=安装完成
FinishedHeadingLabel=[name] 安装完成
FinishedLabel=电脑上已经装好 [name]。%n%n手机第一次使用，先运行开始菜单里的「无线配对（首次使用）」，再启动托盘。
FinishedRestartLabel=需要重启电脑才能完成安装，现在就重启吗？
ClickFinish=点「完成」退出安装程序。
ConfirmUninstall=确定要卸载 %1 吗？%n%n本机保存的设置和手机解锁密码会一起删除。
UninstallStatusLabel=正在从电脑上移除 %1，请稍候……
UninstalledAll=%1 已卸载完成。
UninstalledMost=%1 卸载完成。%n%n有几个文件需要手动删除：%1
UninstalledAndNeedsRestart=卸载完成。需要重启电脑才能彻底删除剩下的文件。
StatusUninstalling=正在卸载 %1……
"""

p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ChineseSimplified.isl")
open(p, "wb").write(isl.replace("\n", "\r\n").encode("gbk"))
print("OK", os.path.getsize(p), "bytes")
