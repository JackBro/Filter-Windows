// Copyright © 2018 CloudVeil Technology, Inc.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.
//

#import "PrivilegeElevation.h"
#import <Foundation/Foundation.h>

bool BlessHelper(const char* label, NSError** errorPtr) {
    bool result = false;
    NSError* error = nil;
    
    AuthorizationRef authRef = nil;
    OSStatus status = AuthorizationCreate(NULL, kAuthorizationEmptyEnvironment, kAuthorizationFlagDefaults, &authRef);
    
    if(status != errAuthorizationSuccess) {
        return false;
    }
    
    AuthorizationItem authItem = { kSMRightBlessPrivilegedHelper, 0, NULL, 0 };
    AuthorizationRights authRights = { 1, &authItem };
    AuthorizationFlags flags = kAuthorizationFlagDefaults |
                                kAuthorizationFlagInteractionAllowed |
                                kAuthorizationFlagPreAuthorize |
                                kAuthorizationFlagExtendRights;
    
    status = AuthorizationCopyRights(authRef, &authRights, kAuthorizationEmptyEnvironment, flags, NULL);
    
    if(status != errAuthorizationSuccess) {
        error = [NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];
    } else {
        CFErrorRef cfError;
        CFStringRef cfLabel = CFStringCreateWithCString(kCFAllocatorDefault, label, kCFStringEncodingUTF8);
        
        result = (bool) SMJobBless(kSMDomainSystemLaunchd, cfLabel, authRef, &cfError);
        
        if(cfLabel != nil) {
            CFRelease(cfLabel);
            cfLabel = nil;
        }
        
        if(!result) {
            error = CFBridgingRelease(cfError);
        }
    }
    
    if(!result && (errorPtr != nil)) {
        if(error != nil) {
            *errorPtr = error;
        }
    }
    
    return result;
}

bool IsEffectiveUserIdRoot() {
    return geteuid() == 0;
}
