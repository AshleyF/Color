Color
=====

Having fun playing around with [colorForth](http://www.colorforth.com/cf.htm) and [GreenArrays](http://www.greenarraychips.com/) architecture. See the [demo](http://www.youtube.com/watch?v=LJoRyxRcj4A&feature=share&list=UUaahWOc75YojOsBjD0RxLlw) and [~~blog series~~](http://blogs.msdn.com/b/ashleyf/archive/tags/color/) - Blog series moved here to Github:

* [Chuck Moore's Creations](Docs/chuck_moores_creations.md)
* [Programming the F18](Docs/programming_the_f18.md)
* [Beautiful Simplicity of colorForth](Docs/beautiful_simplicity.md)
* [Multiply-step Instruction](Docs/multiply_step.md)
* [Simple Variables](Docs/simple_variables.md)

![Editor/Assembler](Docs/images/editor_assembler.png)

The assembler watches for changes to the block files saved by the editor. I leave an instance of this running in one terminal window (right) while working in the editor in another (left). Later I run the machine in a third window.

## Setup

Everything is written in F# and uses solution (`.sln`) and project (`.fsproj`) files compatible with Visual Studio or `dotnet build`. I personally have been using plain Vim (with the [excellent F# bindings](https://github.com/fsharp/fsharpbinding)). Here's setup steps for Ubuntu:

**Install .NET Core**

* Install the [.NET SDK](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu). For example, for Ubuntu 21.04:

```
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt-get update
sudo apt-get install -y apt-transport-https
sudo apt-get update
sudo apt-get install -y dotnet-sdk-5.0
```

**Pull down the project**

    git clone http://github.com/AshleyF/Color

**Build**

    dotnet build Color.sln

Each project produces an executable (`Assembler.exe`, `Editor.exe`, `Machine.exe`) within `bin/`

**Play!**

The Editor edits block files (`/Blocks/*.blk`) while the Assembler waits for changes to block and assembles them (to block 0). Running the Machine executes block 0.

The normal way of working is to run the Editor and Assembler at the same time (in separate tabs or tmux splits, etc.). Each time a block is saved (`s` in the editor), it's assembled. Then run the Machine to try it out.