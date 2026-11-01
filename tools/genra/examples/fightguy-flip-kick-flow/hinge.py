import numpy as np
from scipy.spatial.transform import Rotation as Rot
JOINT_OF = {'LeftKnee': 'LeftLeg', 'RightKnee': 'RightLeg', 'LeftElbow': 'LeftForeArm', 'RightElbow': 'RightForeArm',
            'LeftIndexKnuckle': 'LeftHandIndex1', 'LeftIndexMiddle': 'LeftHandIndex2',
            'RightIndexKnuckle': 'RightHandIndex1', 'RightIndexMiddle': 'RightHandIndex2'}
def hinge_frame(pose, hinge):
    """(straight local rotation, local unit axis of positive flexion) for a hinge joint."""
    j = JOINT_OF[hinge]
    a = pose.copy(); a.set_hinge(hinge, 0); q0 = a.local_rotation(j)
    a.set_hinge(hinge, 30); q1 = a.local_rotation(j)
    rv = (q0.inv() * q1).as_rotvec()
    return q0, rv / np.linalg.norm(rv)
def flex(q0, axis, q):
    rv = (q0.inv() * Rot.from_quat(q)).as_rotvec()
    return float(np.degrees(np.dot(rv, axis))), float(np.degrees(np.linalg.norm(rv - np.dot(rv, axis) * axis)))
