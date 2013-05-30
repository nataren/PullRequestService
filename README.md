PullRequestService
==================

## Overview

The `PullRequestService` listens to notifications from a Github's repository in regards to pull request
creation and closes it if it is targeting the master branch.

## Prerequisites
### To run
1. `Mono` with support for `.NET 4.5`

### To compile
1. Xamarin Studio
2. `F# 3.0` support on Xamarin Studio
3. Access to the `Git` repo <https://bitbucket.org/nataren/pullrequestservice>

## Setup

### Create a Personal API Access Token from an organization's admin user
1. <https://github.com/blog/1509-personal-api-tokens>

### Create a repo's web hook
1. Perform a `POST` request to `https://api.github.com/repos/MindTouch/{REPO}/hooks`
2. Inclue the `Authorization : token {TOKEN}` header in the request
3. The payload is:
``
{ "name": "web",
  "events": ["pull_request"],
  "config" : { "url": "https://{HOSTNAME}:{PORT}/notify", "content_type":"json" }
}
``
Where the config's url must be the `PullRequestService` public `URL` (plus notify's route)

### Create the service's configuration file
Create a file `pr.config` with the following content, and change the values appropriately
``
<config>
  <host>{HOSTNAME}</host>
  <http-port>{PORT}</http-port>
  <script>
    <action verb="POST" path="/host/load?name=PullRequestService" />
	<action verb="POST" path="/host/services">
	  <config>
		<path>{PATH}</path>
		<sid>sid://mindtouch.com/2013/05/pullrequestservice</sid>
		<github.token>{API_TOKEN}</github.token>
	  </config>
	</action>
  </script>
</config>
``

## Run the service
Run the following command inside the service's folder:

`mono mindtouch.host.exe config pr.config`

## Test the service
1. Create a pull request in your repo against the master branch
2. Confirm that the pull request is automatically closed by the `PullRequestService`

