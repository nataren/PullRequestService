PullRequestService
==================

## Overview

The `PullRequestService` listens to notifications from a Github's repository in regards to pull request
creation and closes it if it is targeting the master branch.

## Prerequisites
### Binaries
* Get the from [here] (https://github.com/nataren/PullRequestService/tree/master/src/redist "PullRequestService binaries") 
* Or checkout the `master` branch and get them from the `src\redist` folder

### To run
1. `.NET 4.5` or
2. `Mono` with support for `.NET 4.5`

### To compile
1. `Visual Studio 2012` or
2. `Xamarin Studio` with `F# 3.0` support

## Setup

### Create a Personal API Access Token from an organization's admin user
1. [Github Personal API tokens] (https://github.com/blog/1509-personal-api-tokens)

### Create the service's configuration file
Create a file `pr.config` with the following content, and change the values appropriately

```XML
<config>
	<host>{HOSTNAME}</host>
	<http-port>{PORT}</http-port>
	<script>
		<action verb="POST" path="/host/load?name=PullRequestService" />
		<action verb="POST" path="/host/services">
			<config>
				<path>pr</path>
				<sid>sid://mindtouch.com/2013/05/pullrequestservice</sid>
				<github.token>{TOKEN}</github.token>
				<github.owner>{OWNER}</github.owner>
				<github.repos>{REPOS}</github.repos>
				<public.uri>{OPTIONAL_ROUTE_TO_NOTIFY_END_POINT</public.uri>
			</config>
		</action>
	</script>
</config>
```

## Run the service
Run the following command inside the service's folder:
```SH
mono mindtouch.host.exe config pr.config
```

## Test the service
1. Create a pull request in your repo against the master branch
2. Confirm that the pull request is automatically closed by the `PullRequestService`

