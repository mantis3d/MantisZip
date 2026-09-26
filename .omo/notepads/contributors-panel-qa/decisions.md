# Contributors Panel QA — Decisions

## Verified architectural decisions

### 1. Two independent sections with shared loader
Decision: One `LoadContributorList` method reused for both technical and financial contributors.
Verification: Both calls in `LoadContributors()` use the same shared method. Good DRY pattern.

### 2. CSV format: name,score
Decision: Simple comma-separated format with UTF-8 BOM.
Verification: `File.ReadAllLines` with `Encoding.UTF8`, split by comma, int.TryParse for score.

### 3. WrapPanel ItemWidth=140
Decision: Fixed item width for consistent wrapping.
Verification: Both ItemsControls use `WrapPanel Orientation="Horizontal" ItemWidth="140" ItemHeight="26"`.
At ~680px window width with margins, yields ~4 items per row.

### 4. Empty state via visibility toggle
Decision: hide ItemsControl, show TextBlock.
Verification: `ShowEmptyState()` sets list `Collapsed` and empty `Visible`. XAML defaults: list `Visible`, empty `Collapsed`.

### 5. Order: Technical then Financial
Decision: Technical contributors appear above financial supporters.
Verification: Both code-behind call order and XAML element ordering follow this convention.
