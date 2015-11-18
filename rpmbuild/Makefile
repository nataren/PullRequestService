git_rev=$(shell git rev-parse --short HEAD)
version=$(shell date -u +"%Y%m%d%H%M%S")

all: pullrequestservice

pullrequestservice:
	cd ../src; \
	mkdir -p $(CURDIR)/SOURCES/pullrequestservice-1.0.0/bin; \
	cp -r redist/* $(CURDIR)/SOURCES/pullrequestservice-1.0.0/bin/; \
	tar cvzf $(CURDIR)/SOURCES/pullrequestservice-1.0.0.tar.gz -C $(CURDIR)/SOURCES pullrequestservice-1.0.0; \
	spec=`mktemp`; \
	cat $(CURDIR)/SPECS/pullrequestservice.spec | sed -e 's/^\(Release:\)\(\s\+\)\(.*\)/\1\2$(version)git$(git_rev)/' > $$spec;  \
	echo $$spec; \
	rpmbuild -bb --define "%_topdir $(CURDIR)" $$spec

