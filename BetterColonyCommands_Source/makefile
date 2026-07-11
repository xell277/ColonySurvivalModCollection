# variables
modname = ColonyCommands
zipname = $(modname)-$(version).zip
dllname = $(modname).dll
version = $(shell cat modInfo.json | awk '/"version"/ {print $$3}' | head -1 | sed 's/[",]//g')
zip_files_extra = announcements.example.json antigrief-config.example.json chatcolors.example.json modInfo.json LICENSE README.md preview.png
build_dir = BetterColonyCommands
gamedir ?= /local/games/Steam/steamapps/common/Colony\ Survival
libdir = $(gamedir)/colonyserver_Data/Managed

$(dllname): src/*.cs
	mcs /target:library -nostdlib -r:$(libdir)/Assembly-CSharp.dll,$(libdir)/UnityEngine.CoreModule.dll,$(libdir)/UnityEngine.TextRenderingModule.dll,$(libdir)/Unity.Mathematics.dll,$(libdir)/mscorlib.dll,$(libdir)/System.dll,$(libdir)/System.Core.dll,$(libdir)/System.IO.Compression.dll,$(libdir)/System.IO.Compression.FileSystem.dll,$(libdir)/Newtonsoft.Json.dll,$(libdir)/netstandard.dll,$(libdir)/com.rlabrecque.steamworks.net.dll -out:"$(dllname)" src/*.cs

$(zipname): $(dllname) $(zip_files_extra)
	$(RM) $(zipname)
	mkdir -p $(build_dir)
	cp $(dllname) $(zip_files_extra) $(build_dir)/
	zip -r $(zipname) $(build_dir)
	$(RM) -r $(build_dir)

.PHONY: build default clean all zip install serverlog clientlog
build: $(dllname)

default: build

clean:
	$(RM) $(dllname) $(zipname)

all: clean default zip

zip: $(zipname)

install: build checkjson zip
	$(RM) -r $(gamedir)/gamedata/mods/$(build_dir)
	unzip $(zipname) -d $(gamedir)/gamedata/mods

checkjson: *.json
	find . -type f -name "*.json" | while read f; do echo $$f; json_pp <$$f >/dev/null; done

serverlog:
	less $(gamedir)/gamedata/logs/server/$$(ls -1rt $(gamedir)/gamedata/logs/server | tail -1)

clientlog:
	less $(gamedir)/gamedata/logs/client/$$(ls -1rt $(gamedir)/gamedata/logs/client | tail -1)
