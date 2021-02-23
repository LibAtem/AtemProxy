# AtemProxy

### Warning

This is an early prototype currently. It has not been tested beyond my sofa, so could freeze up, kill connections or other bad things. Please wait a bit before trying to use it in production as it could go very very badly.

### Introduction

This is a small tool designed to help workaround the connection limit of Blackmagic ATEM mixers.

Most models of the mixers have a maximum of 5 connections. This can often not be enough when adding in control clients, automation software and tally software.

The basic principle is that this bit of software will connect to the ATEM once, and will then be the device that some of your clients connect to.

Note: It is recommended to connect directly to the atem for clients which are important to work 100% of the time, or those which do large amounts of media uploads.

### Device support
The primary focus is to support 8.0+ fully. Older versions may work, but are no longer recommended.
7.2 should work pretty well, but is not feature complete.

Currently, 8.0.0 - 8.5.3 should work fully. Newer firmware may work, but could have issues with clients connecting after it has been running for a while.

### Download
When it is ready

### Usage
Coming soon


### Related Projects
Uses LibATEM for connection management and command parsing [LibAtem](https://github.com/LibAtem/LibAtem)

### License

LibAtem and AtemProxy are distributed under the GNU Lesser General Public License LGPLv3 or higher, see the file LICENSE for details.


