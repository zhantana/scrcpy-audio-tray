# 自定义安装目录

## Why

安装时用户无法选择安装位置，只能固定装到 `%LOCALAPPDATA%\ScrcpyAudioTray`。

## What Changes

- 安装包强制显示「选择安装位置」页面，默认目录不变，用户可自行指定
- 程序全部路径基于 `$PSScriptRoot`，跟随安装目录，无需改代码

## 非目标

- 不改默认目录、不改提权方式、不做目录校验
