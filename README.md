PullRequestService
==================

## Overview

The `PullRequestService` listens to notifications from a Github's repository in regards to pull request
creation and closes it if it is targeting the master branch.

## License
Open Source under the Apache License Version 2.0

## Prerequisites
### Binaries
* Get them from [releases page] (https://github.com/nataren/PullRequestService/releases "PullRequestService binaries") 
* Or checkout the `master` branch and get them from the `src\redist` folder

### To run
1. `.NET 4.5` with `F# 3.0` support (or `VS 2012` with `F# 3.0` support)  or
2. `Mono` with support for `.NET 4.5` and `F# 3.0`

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
                <github.frozen.branches>
                    <repo name="{REPO_NAME}">
                        <branch>{BRANCH_NAME}</branch>
                        .
                        .
                        .
                    </repo>
                    .
                    .
                    .
                </github.frozen.branches>
				<public.uri>{ROUTE_TO_NOTIFY_END_POINT</public.uri>
				<merge.retries>{NUMBER_OF_MERGE_RETRIES}</merge.retries>
                <merge.ttl>{MILLISECONDS_TO_WAIT_BEFORE_TRYING_TO_MERGE_PULL_REQUEST_AGAIN}</merge.ttl>
                <mergeability.retries>{NUMBER_OF_MERGEABILITY_CHECK_RETRIES}</mergeability.retries>
                <mergeability.ttl>{MILLISECONDS_TO_WAIT_BEFORE_CHECKING_MERGEABILITY}</mergeability.ttl>
                <github2youtrack>{COMMA_SEPARATED_LIST_OF_COLON_SEPARATED_GITHUB_2_YOUTRACK_USERNAMES_MAPPING}</github2youtrack>
                <youtrack.hostname>{YOUTRACK_HOSTNAME}</youtrack.hostname>
                <youtrack.username>{YOUTRACK_USERNAME}</youtrack.username>
                <youtrack.password>{YOUTRACK_PASSWORD}</youtrack.password>
                <archive.branches.ttl>{HOW_OFTEN_TO_ARCHIVE_BRANCHES_IN_MILLISECONDS}</archive.branches.ttl>
                <archive.branches.keep>{HOW_MANY_RELEASE_BRANCHES_TO_KEEP_AROUND}</archive.branches.keep>
				<to.email>{TARGET_EMAIL_ADDRESSES}</to.email>
				<from.email>{SES_ENABLED_EMAIL_ADDRESS}</from.email>
				<aws.region>{REGION}</aws.region>
			</config>
		</action>
	</script>
</config>
```

`TARGET_EMAIL_ADDRESSES` in the `to.email` support multiple comman separated values, for example

```
	<to.email>foo@bar.com, baz@bar.com</to.email>
```

## Run the service
Run the following command inside the service's folder:
```SH
mono mindtouch.host.exe config pr.config
```

## Test the service
1. Create a pull request in your repo against the master branch
2. Confirm that the pull request is automatically closed by the `PullRequestService`

