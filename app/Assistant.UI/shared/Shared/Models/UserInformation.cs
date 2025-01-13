﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Models;

public record class UserInformation(bool IsIdentityEnabled, string UserName,string UserId, string SessionId, IEnumerable<ProfileSummary> Profiles, IEnumerable<string> Groups);
