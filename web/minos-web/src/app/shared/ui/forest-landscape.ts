import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Static, decorative brand artwork; never conveys content or accepts input. */
@Component({
  selector: 'minos-forest-landscape',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  templateUrl: './forest-landscape.html',
  styleUrl: './forest-landscape.css',
})
export class ForestLandscape {
  // Varied scale/spacing avoids a repeating geometric pattern in the treeline.
  protected readonly trees = [
    [42, 376, 0.72], [88, 367, 1.1], [122, 358, 0.8], [167, 345, 1.35],
    [208, 342, 0.82], [246, 325, 1.05], [283, 324, 0.7], [316, 315, 1.4],
    [361, 313, 0.95], [399, 314, 1.15], [439, 325, 0.72], [474, 331, 1.25],
    [519, 345, 0.9], [554, 352, 1.05], [594, 361, 0.75], [632, 369, 1.3],
    [681, 371, 0.88], [714, 362, 1.1], [750, 351, 0.7], [784, 338, 1.25],
    [826, 331, 0.95], [865, 314, 1.4], [912, 309, 0.8], [945, 294, 1.1],
    [980, 284, 0.7], [1015, 277, 1.25], [1058, 276, 0.9], [1092, 263, 1.35],
    [1138, 251, 0.8], [1171, 244, 1.05], [1210, 224, 1.4], [1258, 226, 0.85],
    [1293, 213, 1.2], [1334, 209, 0.75], [1371, 198, 1.1], [1411, 184, 1.3],
  ] as const;
}
