#pragma once

#include "ConflictReason.h"

using namespace System;

namespace FilterNativeWindows {
	public ref class ConflictDetection
	{
    public:
        ConflictReason SearchConflictReason();

	};
}
