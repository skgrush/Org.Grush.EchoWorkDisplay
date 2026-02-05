
$fs = 0.5;

// Hadamard multiplication, aka component-wise multiplication.
// https://github.com/openscad/openscad/issues/821#issuecomment-647706082
function hadamard(a,b) =
    !(is_list(a))
        ? a*b
        : [
            for(i = [0:len(a)-1])
              hadamard(a[i],b[i])
          ];
          
render_case = true;
render_screen = true;
render_pi = true;

/////////////////////////////////////////////////
//////////// physical parts variables ///////////
/////////////////////////////////////////////////


pcbDimensions = [51, 21, 1];
headerRowPitch = 17.78;
headerPinPitch = 2.54;
headerInsulationDimensions = [
    20 * headerPinPitch,
    2.49,
    2.29
];

headerPinToEdge = [
    (pcbDimensions.x - (headerPinPitch*19)) / 2,
    (pcbDimensions.y - headerRowPitch) / 2
];

mountHoleShortEdgeDistance = 2;
mountHolesPitch = [
    pcbDimensions.x - 2 * mountHoleShortEdgeDistance ,
    11.4
];
mountHoleToEdge = [
    mountHoleShortEdgeDistance,
    (pcbDimensions.y - mountHolesPitch.y) / 2
];

pcbCenterCoordinates = [
    pcbDimensions.x / 2 - headerPinToEdge.x,
    -pcbDimensions.y / 2 + headerPinToEdge.y
];

display_pcbDimensions = [
  52,
  35,
  1
];

display_moduleDimensions = [
    display_pcbDimensions.x - 4,
    display_pcbDimensions.y,
    3.9
];
display_headerInsulationDimensions = [
    headerInsulationDimensions.x,
    2.49,
    9
];

display_pcbCenterCoordinates = [
    pcbCenterCoordinates.x,
    pcbCenterCoordinates.y,
    headerInsulationDimensions.z + 
        display_headerInsulationDimensions.z + 
        display_pcbDimensions.z / 2
];
display_moduleCenterCoordinates = [
    pcbCenterCoordinates.x,
    pcbCenterCoordinates.y,
    display_pcbCenterCoordinates.z + 
        display_pcbDimensions.z / 2 +
        display_moduleDimensions.z / 2
];


// Pi Pico mechanical drawing: https://datasheets.raspberrypi.com/pico/pico-datasheet.pdf#page=7
// Pi Pico 2 mech drawing:  https://datasheets.raspberrypi.com/pico/pico-2-datasheet.pdf#page=8

header0Coordinates = [
    headerPinPitch / -2,
    headerInsulationDimensions.y * -0.5,
    0
];
header1Coordinates = [
    headerPinPitch / -2,
    headerInsulationDimensions.y * -0.5 - headerRowPitch,
    0
];


mountHole0coord = hadamard(
  mountHoleToEdge - headerPinToEdge,
  [1, -1]
);

mountHoleCoordinates = [
  mountHole0coord,
  mountHole0coord + [mountHolesPitch.x, 0],
  mountHole0coord + [mountHolesPitch.x, -mountHolesPitch.y],
  mountHole0coord + [0, -mountHolesPitch.y],
];


////////////////////////////////////////////
/////////// DRAW PCB AND HEADERS ///////////
////////////////////////////////////////////


if (render_pi) {

    translate(header0Coordinates)
        color("gray")
        cube(
            headerInsulationDimensions,
            center = false
        );

    translate(header1Coordinates)
        color("gray")
        cube(
            headerInsulationDimensions,
            center = false
        );
        

    rotate(180, v=[1,0,0])
        import("./picow.stl");    

}
    
if (render_screen) {

    translate(header0Coordinates + [0,0,headerInsulationDimensions.z])
        color("lightgray")
        cube(
            display_headerInsulationDimensions,
            center = false
        );

    translate(header1Coordinates + [0,0,headerInsulationDimensions.z])
        color("lightgray")
        cube(
            display_headerInsulationDimensions,
            center = false
        );
        
    translate(display_pcbCenterCoordinates)
        color("blue")
        cube(
            display_pcbDimensions,
            center = true
        );
        
    translate(display_moduleCenterCoordinates)
        color("darkblue")
        cube(
            display_moduleDimensions,
            center = true
        );

}



////////////////////////////////
///////////// CASE /////////////
////////////////////////////////

standoffHeight = 5;
case_color = "orange";
case_wallThickness = 4;
case_baseThickness = 6;

case_displayOffset = 1;
case_piSpacing = 0.5;

case_usbHoleOffset = -3.5;

microUsbBootWidth = 10;
microUsbBootHeight = 10;

case_innerX = display_pcbDimensions.x + 2*case_piSpacing;//case_displayOffset;
case_innerY = display_pcbDimensions.y + 2*case_piSpacing;//case_displayOffset;
case_innerZ = standoffHeight
    + display_pcbDimensions.z
    + headerInsulationDimensions.z
    + display_headerInsulationDimensions.z
    + display_pcbDimensions.z
    + display_moduleDimensions.z;

case_baseDimensions = [
    case_innerX + 2 * case_wallThickness,
    case_innerY + 2 * case_wallThickness,
    case_baseThickness
];

case_xWallDimensions = [
    case_innerX + 2 * case_wallThickness,
    case_wallThickness,
    case_innerZ
];
case_yWallDimensions = [
    case_wallThickness,
    case_innerY + 2 * case_wallThickness,
    case_innerZ
];


case_usbHoleDimensions = [
    case_wallThickness+0.01,
    microUsbBootWidth,
    microUsbBootHeight
];

if (render_case) {

    // draw mount points on holes
    for (i = [0:3]) {

        translate(concat( mountHoleCoordinates[i], [-standoffHeight/2 - display_pcbDimensions.z - case_baseThickness/2]) )
        difference() {
            color(case_color)
            cylinder(standoffHeight+case_baseThickness, d = 3.5, center=true);
            cylinder(standoffHeight+case_baseThickness + 0.2, d = 2.0, center=true);
        }
    }
    // draw case base
    translate(
        concat(
            pcbCenterCoordinates,
            [
                -display_pcbDimensions.z 
                - standoffHeight
                - case_baseThickness/2
            ]
        )
    )
    color(case_color)
    cube(case_baseDimensions, center=true);


    // draw case walls X
    translate([
        pcbCenterCoordinates.x,
        pcbCenterCoordinates.y + case_innerY/2 + case_wallThickness/2,
            -display_pcbDimensions.z 
            - standoffHeight
            - case_baseThickness/2
            + case_xWallDimensions.z/2
        ]
    )
    color(case_color)
    cube(case_xWallDimensions, center=true);
    translate([
        pcbCenterCoordinates.x,
        pcbCenterCoordinates.y - case_innerY/2 - case_wallThickness/2,
            -display_pcbDimensions.z 
            - standoffHeight
            - case_baseThickness/2
            + case_xWallDimensions.z/2
        ]
    )
    color(case_color)
    cube(case_xWallDimensions, center=true);

    // draw case wall Y Right
    translate([
        pcbCenterCoordinates.x + case_innerX/2 + case_wallThickness/2,
        pcbCenterCoordinates.y,
            -display_pcbDimensions.z
            - standoffHeight
            - case_baseThickness/2
            + case_xWallDimensions.z/2
        ]
    )
    color(case_color)
    cube(case_yWallDimensions, center=true);
    // draw case wall Y Left
    translate([
        pcbCenterCoordinates.x - case_innerX/2 - case_wallThickness/2,
        pcbCenterCoordinates.y,
            -display_pcbDimensions.z 
            - standoffHeight
            - case_baseThickness/2
            + case_xWallDimensions.z/2
        ]
    )
    color(case_color)
    difference() {
        cube(case_yWallDimensions, center=true);
        translate([0,0,case_usbHoleOffset])
            cube(case_usbHoleDimensions, center=true);
    }
    
    
    
}