﻿CREATE TABLE `user` (
	`id`	GUID NOT NULL PRIMARY KEY,
	`facebookId`	TEXT NOT NULL UNIQUE,
	`fullName`	TEXT NOT NULL
);