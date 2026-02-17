#!/bin/sh
set -e

readonly DOWNLOAD_URL="https://launcher.sashok724.net/download"
readonly LIBRARIES_DIR="libraries"
readonly UPDATES_DIR="updates"
readonly JRE_VERSION="8u202"

# Create dirs
mkdir -p $LIBRARIES_DIR $LIBRARIES_DIR/hikaricp $UPDATES_DIR

# Download LaunchServer
echo "Downloading LaunchServer.jar"
curl -o LaunchServer.jar $DOWNLOAD_URL/LaunchServer.jar

# Download JAnsi
echo "Downloading JAnsi (Colored console support)"
if [ ! -f $LIBRARIES_DIR/jansi.jar ]; then
	curl -o $LIBRARIES_DIR/jansi.jar $DOWNLOAD_URL/$LIBRARIES_DIR/jansi.jar
fi

# Download JLine2
echo "Downloading JLine2 (Better console support)"
if [ ! -f $LIBRARIES_DIR/jline2.jar ]; then
	curl -o $LIBRARIES_DIR/jline2.jar $DOWNLOAD_URL/$LIBRARIES_DIR/jline2.jar
fi

# Download HikariCP
echo "Downloading HikariCP (JDBC connection pooling)"
if [ ! -f $LIBRARIES_DIR/hikaricp/hikaricp.jar ]; then
	curl -o $LIBRARIES_DIR/hikaricp/hikaricp.jar $DOWNLOAD_URL/$LIBRARIES_DIR/hikaricp/hikaricp.jar
	curl -o $LIBRARIES_DIR/hikaricp/javassist.jar $DOWNLOAD_URL/$LIBRARIES_DIR/hikaricp/javassist.jar
	curl -o $LIBRARIES_DIR/hikaricp/slf4j-api.jar $DOWNLOAD_URL/$LIBRARIES_DIR/hikaricp/slf4j-api.jar
	curl -o $LIBRARIES_DIR/hikaricp/slf4j-simple.jar $DOWNLOAD_URL/$LIBRARIES_DIR/hikaricp/slf4j-simple.jar
fi

# Download MySQL
echo "Downloading MySQL Connector (Authentication support)"
if [ ! -f $LIBRARIES_DIR/mysql.jar ]; then
	curl -o $LIBRARIES_DIR/mysql.jar $DOWNLOAD_URL/$LIBRARIES_DIR/mysql.jar
fi

# Download Launch4J
echo "Downloading Launch4J (EXE binary building support)"
if [ ! -d $LIBRARIES_DIR/launch4j ]; then
	if [ "$(uname)" == "Darwin" ]; then
		curl -o $LIBRARIES_DIR/launch4j.tar.gz $DOWNLOAD_URL/$LIBRARIES_DIR/launch4j-osx.tar.gz
	else
		curl -o $LIBRARIES_DIR/launch4j.tar.gz $DOWNLOAD_URL/$LIBRARIES_DIR/launch4j.tar.gz
	fi
	tar -xf $LIBRARIES_DIR/launch4j.tar.gz -C $LIBRARIES_DIR
	chmod -R +x $LIBRARIES_DIR/launch4j/bin
	rm $LIBRARIES_DIR/launch4j.tar.gz
fi

# Download JVMs
echo "Downloading Java $JRE_VERSION JVMs"
if [ ! -d $UPDATES_DIR/jre-$JRE_VERSION-win32 ] || [ ! -d $UPDATES_DIR/jre-$JRE_VERSION-win64 ] || [ ! -d $UPDATES_DIR/jre-$JRE_VERSION-linux32 ] || [ ! -d $UPDATES_DIR/jre-$JRE_VERSION-linux64 ] || [ ! -d $UPDATES_DIR/jre-$JRE_VERSION-macosx ]; then
	curl -o $UPDATES_DIR/jre.tar.gz $DOWNLOAD_URL/jre-$JRE_VERSION.tar.gz
	tar --warning=no-unknown-keyword -xf $UPDATES_DIR/jre.tar.gz -C $UPDATES_DIR
	rm $UPDATES_DIR/jre.tar.gz
fi

# Ready to use!
echo "Ready to use!"
