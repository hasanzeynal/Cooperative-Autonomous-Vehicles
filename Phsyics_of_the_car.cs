using System;
using UnityEngine;

[Serializable]

public class WheelProperties {

    public int wheelState = 1; //if set to 1 it means wheels are locked, if set to 0, it is not locked, it is like a free wheel

    [HideInInspector] public float bibirectional = 0; //it is optional, advanced feature

    public Vector3 localPosition;

    public float turnAngle = 30; //max steer angle for this wheel

    [HideInInspector] public float lastSuspensionLength = 0.0f;

    [HideInInspector] public Vector3 localSlipDirection;

    [HideInInspector] public Vector3 worldSlipDirection;

    [HideInInspector] public Vector3 suspensionForceDirection;

    [HideInInspector] public Vector3 wheelWorldPosition;

    [HideInInspector] public float wheelCircumference;

    [HideInInspector] public float torque = 0.0f;

    [HideInInspector] public Rigidbody parentRigidbody;

    [HideInInspector] public GameObject wheelObject;

    [HideInInspector] public float hitPointForce;

    [HideInInspector] public Vector3 localVelocity;

}


public class car: MonoBehaviour {
    [Header("WheelSetup")]

    public GameObject wheelPrefab;

    public WheelProperties[] wheels;

    public float wheelSize = 0.53f; //radius of the wheel

    public float maxTorque = 450f; //maximum engine torque

    public float wheelGrip = 12f;

    public float maxGrip = 12f;

    public float frictionCoWheel = 0.022f;  //rolling friction

    [Header("Suspension")]

    public float suspensionForce = 90f; //spring constant

    public float dampAmount = 2.5f; //damping constant

    public float suspensionForceClamp = 200f; //cap on total suspension force

    [Header("Car Mass")]

    public float massInKg = 100f; //might be incorporated, but not strict one



    //parameters that updated each frame
    [HideInInspector] public Vector2 input = Vector2.zero; //horizantal = steering, vertical = gas/brake

    [HideInInspector] public bool forwards = false;

    private Rigidbody rb;


    void Start() {
        //grab or add rigid body
        rb = GetComponent<Rigidbody>();

        if (!rb) rb = gameObject.AddComponent<Rigidbody>();

        //slight tweak to inertia if desired
        rb.inertiaTensor = 1.0f * rb.inertiaTensor;

        //create each wheel
        if (wheels != null) 
        {
            for (int i =0;i < wheels.Length; i++)
            {
                WheelProperties w = wheels[i];

                //and lets convert localPosition consistently
                Vector3 parentRelativePosition = transform.InverseTransformPoint(transform.TransformPoint(w.localPosition));
                w.localPosition = parentRelativePosition;

                //instantiate the visual wheel
                w.wheelObject = Instantiate(wheelPrefab, transform);
                w.wheelObject.transform.localPosition = w.localPosition;
                w.wheelObject.transform.eulerAngles   = transform.eulerAngles;
                w.wheelObject.transform.localScale = 2f * new Vector3(wheelSize, wheelSize, wheelSize);

                //calculate wheel circumference for rottion logic
                w.wheelCircumference = 2f * Mathf.PI * wheelSize;


                w.parentRigidbody = rb;
            }
        }
    }

    void Update() {
        //gather inputs
        input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));

    }

    void FixedUpdate()
    {
        if (wheels == null || wheels.Length == 0) return;

        foreach (var wheel in wheels)
        {
            if (!wheel.wheelObject) continue;

            //for easy reference
            Transform wheelObj = wheel.wheelObject.transform;
            Transform wheelVisual = wheelObj.GetChild(0);

            //calculatye if steer wheel is 1
            if (wheel.wheelState == 1)
            {
                float targetAngle = wheel.turnAngle * input.x; //left right
                Quaternion targetRot = Quaternion.Euler(0, targetAngle, 0);

                //lerp to the new steer angle
                wheelObj.localRotation = Quaternion.Lerp(
                    wheelObj.localRotation,
                    targetRot,
                    Time.fixedDeltaTime * 100f
                );
            }
            else if (wheel.wheelState == 0 && rb.velocity.magnitude > 0.04f) 
            {
                //fpr free wheels, optionally align them in direction of motion

                RaycastHit tmpHit;
                if (Physics.Raycast(transform.TransformPoint(wheel.localPosition),
                                    -transform.up,
                                    out tmpHit,
                                    wheelSize * 2f))
                {
                    Quaternion aim = Quaternion.LookRotation(rb.GetPointVelocity(tmpHit.point), transform.up);
                    wheelObj.rotation = Quaternion.Lerp(wheelObj.rotation, aim, Time.fixedDeltaTime * 100f);
                }
            }

            //determine the world position of this wheel and velocity at that point
            wheel.wheelWorldPosition = transform.TransformPoint(wheel.localPosition);
            Vector3 velocityAtWheel = rb.GetPointVelocity(wheel.wheelWorldPosition);


            //so we dont have to manually rotate by turnangle again
            wheel.localVelocity = wheelObj.InverseTransformDirection(velocityAtWheel);

            //ENGINE + friction in the wheel's local Z axis
            //"wheel.torque" can be something like (vertical input * maxTorque), etc.
            //Adjust or clamp as needed:
            wheel.torque = Mathf.Clamp(input.y, -1f, 1f) * maxTorque / massInKg;

            //rolling friction
            float rollingFrictionForce = -frictionCoWheel * wheel.localVelocity.z;

            //Lateral friction tries to cancel sideways slip
            float lateralFriction = -wheelGrip * wheel.localVelocity.x;
            lateralFriction = Mathf.Clamp(lateralFriction, -maxGrip, maxGrip);

            //engine force ( F = torque / radius)
            float engineForce = wheel.torque / wheelSize;

            //Combine them in local space
            Vector3 totalLocalForce = new Vector3(
                lateralFriction,
                0f,
                rollingFrictionForce + engineForce
            );

            wheel.localSlipDirection = totalLocalForce;


            //transform to thecworld space
            Vector3 totalWorldForce = wheelObj.TransformDirection(totalLocalForce);
            wheel.worldSlipDirection = totalWorldForce;


            //check if the wheel is moving forward in its own local frame
            forwards = (wheel.localVelocity.z > 0f); //changed it

            // SUSPENSION (spring + damper)
            RaycastHit hit;
            if (Physics.Raycast(wheel.wheelWorldPosition, -transform.up, out hit, wheelSize * 2f))
            {
                //how much the spring is compressed
                float raylen = wheelSize * 2f;
                float compression = raylen - hit.distance;

                //damping is difference from last frame
                float damping = (wheel.lastSuspensionLength - hit.distance) * dampAmount;
                float springForce = (compression + damping) * suspensionForce;

                //clamp it
                springForce = Mathf.Clamp(springForce, 0f, suspensionForceClamp);

                //direction is the surface normal
                Vector3 springDir = hit.normal * springForce;
                wheel.suspensionForceDirection = springDir;

                //apply total forces at contact
                rb.AddForceAtPosition(springDir + totalWorldForce, hit.point);

                //move wheel visuals to the contact point + offset
                wheelObj.position = hit.point + transform.up * wheelSize;

                //store for damping next frame
                wheel.lastSuspensionLength = hit.distance;

            }
            else
            {
                //if not hitting anything, just position the wheel under local anchor
                wheelObj.position = wheel.wheelWorldPosition - transform.up * wheelSize;
            }

            //roll the wheel visually like in the original code
            // we will get the forward speed in the wheelsObj's local space:
            Vector3 forwardInWheelSpace = wheelObj.InverseTransformDirection(rb.GetPointVelocity(wheel.wheelWorldPosition));

            //convert that local zspeed into a rotation about x
            float wheelRotationSpeed = forwardInWheelSpace.z * 360f / wheel.wheelCircumference;

            //rotate the visual child
            wheelVisual.Rotate(Vector3.right, wheelRotationSpeed * Time.fixedDeltaTime, Space.Self);
        }
    }
}