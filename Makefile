PLUGIN_NAME := Jellyfin.Plugin.RemoteAuth
JELLYFIN_PLUGIN_DIR ?= /var/lib/jellyfin/plugins/RemoteAuth
BUILD_DIR := dist

.PHONY: build clean install docker-build docker-install package

# Build with local .NET SDK
build:
	dotnet publish $(PLUGIN_NAME)/$(PLUGIN_NAME).csproj -c Release -o $(BUILD_DIR)

clean:
	rm -rf $(BUILD_DIR)
	dotnet clean 2>/dev/null || true

# Build + copy to Jellyfin plugin directory
install: build
	mkdir -p $(JELLYFIN_PLUGIN_DIR)
	cp $(BUILD_DIR)/*.dll $(BUILD_DIR)/meta.json $(JELLYFIN_PLUGIN_DIR)/

# Build via Docker (no .NET SDK required)
docker-build:
	docker build --target artifact --output type=local,dest=$(BUILD_DIR) .

docker-install: docker-build
	mkdir -p $(JELLYFIN_PLUGIN_DIR)
	cp $(BUILD_DIR)/*.dll $(BUILD_DIR)/meta.json $(JELLYFIN_PLUGIN_DIR)/

# Build the installable zip package
package:
	docker build --target package --output type=local,dest=$(BUILD_DIR) .
	@echo "Package ready: $(BUILD_DIR)/remote-auth.zip"
