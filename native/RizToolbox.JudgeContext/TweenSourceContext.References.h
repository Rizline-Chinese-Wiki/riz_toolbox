#pragma once
#include "TweenSourceContext.h"

// Same-helper operation-finally seam. Invoke while the corresponding actual
// managed operation scope is still current, before its completion callback can
// dispose/pop that scope. Sends Abnormal for unfinished captured source scopes;
// does not pop the native parent, alter the original exception or free roots.
// Ordinary SourceOperationExit remains the final native ticket/Unknown check.
std::uint32_t RizTweenSourceOperationPrepareExitReferences(
    const RizTweenSourceOperationTicketV1* actualTicket);
