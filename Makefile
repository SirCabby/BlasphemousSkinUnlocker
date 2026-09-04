# Blasphemous Skin Unlocker — build, versioning and packaging.
#
#   make build           build the plugin (Release)
#   make version         print the current version
#   make version 1.2.3   set the project version to 1.2.3
#   make package         build and zip dist/BlasSkinUnlocker_v<version>.zip
#   make clean           remove build output and dist/
#
# The version lives in the VERSION file only. The build reads it from there for
# the assembly version and for the [BepInPlugin] attribute, so `make version`
# never has to touch the source.

MOD_NAME := BlasSkinUnlocker
CONFIG   := Release
VERSION  := $(shell cat VERSION 2>/dev/null)
DLL      := bin/$(CONFIG)/$(MOD_NAME).dll
DIST     := dist
STAGE    := $(DIST)/$(MOD_NAME)_v$(VERSION)
ZIP      := $(DIST)/$(MOD_NAME)_v$(VERSION).zip

.DEFAULT_GOAL := help
.PHONY: help build clean version package

# `make version 1.2.3` passes 1.2.3 as a second goal; declare it as a do-nothing
# target so make doesn't fail with "No rule to make target '1.2.3'".
ifeq (version,$(firstword $(MAKECMDGOALS)))
NEW_VERSION := $(word 2,$(MAKECMDGOALS))
ifneq ($(NEW_VERSION),)
$(eval $(NEW_VERSION):;@:)
endif
endif

help:
	@echo "Blasphemous Skin Unlocker (v$(VERSION))"
	@echo
	@echo "  make build           build the plugin (Release)"
	@echo "  make version         print the current version"
	@echo "  make version 1.2.3   set the project version to 1.2.3"
	@echo "  make package         build and zip $(DIST)/$(MOD_NAME)_v<version>.zip"
	@echo "  make clean           remove build output and $(DIST)/"

build:
	dotnet build -c $(CONFIG)

version:
ifeq ($(NEW_VERSION),)
	@echo "$(VERSION)"
else
	@echo "$(NEW_VERSION)" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$$' \
	  || { echo "error: version must be MAJOR.MINOR.PATCH (e.g. 1.2.3), got '$(NEW_VERSION)'" >&2; exit 1; }
	@printf '%s\n' "$(NEW_VERSION)" > VERSION
	@echo "version: $(VERSION) -> $(NEW_VERSION)"
endif

package: build
	@test -n "$(VERSION)" || { echo "error: VERSION file is missing or empty" >&2; exit 1; }
	@rm -rf "$(STAGE)" "$(ZIP)"
	@mkdir -p "$(STAGE)/BepInEx/plugins"
	@cp "$(DLL)" "$(STAGE)/BepInEx/plugins/"
	@cp README.md LICENSE "$(STAGE)/"
	@cd "$(STAGE)" && zip -rq "../$(notdir $(ZIP))" .
	@rm -rf "$(STAGE)"
	@echo "packaged $(ZIP)"

clean:
	dotnet clean -c $(CONFIG) --nologo -v quiet || true
	rm -rf bin obj "$(DIST)"
