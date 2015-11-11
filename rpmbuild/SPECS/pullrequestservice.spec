Name:		pullrequestservice
Version:	1.0.0
Release:	2%{?dist}
Summary:	MindTouch Pull Request Service 

Group:		Applications/Internet
License:	Commercial
URL:		http://www.mindtouch.com
Source0:	%{name}-%{version}.tar.gz
Source1:	%{name}.initd
Source2:	%{name}.sysconfig
Source3:	%{name}.config
BuildRoot:	%(mktemp -ud %{_tmppath}/%{name}-%{version}-%{release}-XXXXXX)
BuildArch:  noarch

Requires:	daemonize, chkconfig, initscripts
Provides:	pullrequestservice
AutoReqProv:	no

%global __requires_exclude_from ^%{_datadir}/%{name}/.*$

%description
Mindtouch Pull Request Service

%prep
%setup -q

%build

%install
rm -rf %{buildroot}

# /usr/share/mindtouch-api
%{__mkdir_p} %{buildroot}%{_datadir}/%{name}
cp -r bin %{buildroot}%{_datadir}/%{name}/

# /etc/init.d/pullrequestservice
%{__mkdir_p} %{buildroot}%{_sysconfdir}/init.d
%{__install} -m0755 %{SOURCE1} %{buildroot}%{_sysconfdir}/init.d/%{name}

# /etc/sysconfig/pullrequestservice
%{__install} -Dp -m0755 %{SOURCE2} %{buildroot}%{_sysconfdir}/sysconfig/%{name}

%{__mkdir_p} %{buildroot}%{_sysconfdir}/%{name}
%{__install} -Dp -m0640 %{SOURCE3} %{buildroot}%{_sysconfdir}/%{name}/%{name}.config

# /var/log/pullrequestservice
%{__mkdir_p} %{buildroot}%{_localstatedir}/log/%{name}
%{__mkdir_p} %{buildroot}%{_localstatedir}/run/%{name}

%clean
rm -rf %{buildroot}

%pre
getent group %{name} >/dev/null || groupadd -r %{name}
getent passwd %{name} >/dev/null || \
    useradd -r -g %{name} -d %{_datadir}/%{name} \
    -s /sbin/nologin -c "%{name} daemon" %{name}

%post
/sbin/chkconfig --add %{name}

%preun
if [ "$1" = "0" ]; then
    service %{name} stop >/dev/null 2>&1 ||:
    /sbin/chkconfig --del %{name}
    exit 0
fi

%files
%defattr(-,root,root,-)
%dir %attr(-,%{name},%{name}) %{_localstatedir}/log/%{name}
%dir %attr(-,%{name},%{name}) %{_localstatedir}/run/%{name}
%{_datadir}/%{name}
%config(noreplace) %{_sysconfdir}/sysconfig/%{name}
%dir %attr(-,root,%{name}) %{_sysconfdir}/%{name}/
%config(noreplace) %attr(-,root,%{name}) %{_sysconfdir}/%{name}/%{name}.config
%{_sysconfdir}/init.d/%{name}

%changelog
* Mon Nov 9 2014 PeteE <petee@mindtouch.com>
 - initial packaging for pullrequestservice

