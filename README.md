![.NET Core Build and Test](https://github.com/Cingulara/openrmf-api-save/workflows/.NET%20Core%20Build%20and%20Test/badge.svg)

# openrmf-api-save
This is the OpenRMF Save API for saving a checklist and its metadata. It has two calls.

* POST to / to save a new document
* PUT to /artifact/{id} to update an artifact record
* PUT to /artifact/{artifactId}/vulnid/{vulnid} to update a vulnerability record in a checklist
* POST to upgradechecklist/system/{systemGroupId}/artifact/{artifactId} to upgrade a checklist to a new version
* DELETE to /artifact/{id} to remove an artifact
* DELETE to /system/{id} to remove a system and all artifacts
* DELETE to system/{id}/artifacts to remove 1 or more artifact records in a system
* POST to /system to make a new system record
* PUT to /system/{systemGroupId} to update a system record
* /swagger/ gives you the API structure.

## Making your local Docker image
* make build
* make latest

## creating the user
* ~/mongodb/bin/mongo 'mongodb://root:myp2ssw0rd@localhost'
* use admin
* db.createUser({ user: "openrmf" , pwd: "openrmf1234!", roles: ["readWriteAnyDatabase"]});
* use openrmf
* db.createCollection("Artifacts");

## connecting to the database collection straight
~/mongodb/bin/mongo 'mongodb://openrmf:openrmf1234!@localhost/openrmf?authSource=admin'

## Messaging Platform
Using NATS from Synadia to have a messaging backbone and eventual consistency. Currently publishing to these known items:
* openrmf.save.new with payload (new Guid Id)
* openrmf.save.update with payload (new Guid Id)
* openrmf.upload.new with payload (new Guid Id)
* openrmf.upload.update with payload (new Guid Id)
* openrmf.template.read to get a template to match to a SCAP scan XML file

### How to run NATS
* docker run --rm --name nats-main -p 4222:4222 -p 6222:6222 -p 8222:8222 nats
* this is the default and lets you run a NATS server version 1.2.0 (as of 8/2018)
* just runs in memory and no streaming (that is separate)