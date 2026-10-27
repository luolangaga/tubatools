
Welcome to the Great Internet Mersenne Prime Search!

To use this program you must agree to the terms and conditions,
prize rules, etc. at https://mersenne.org/legal/

The GIMPS web site is at https://mersenne.org
Help is available at https://mersenneforum.org
My email address is woltman@alum.mit.edu


FILE LIST
---------

readme.txt	This file.
prime95.exe	The windows program to trial factor and primality test Mersenne numbers.
mprime		The Linux program to trial factor and primality test Mersenne numbers.
libgmp*		A library containing the GNU multi-precision math package.
libhwloc*	A library containing routines to analyze your hardware (cores, cache sizes, etc.)
libcurl*	A library containing routines to send and receive internet messages.
libcrypto*, libssl*	Libraries required by libcurl.
		On some OSes you may need to copy libraries to the system library directory.
whatsnew.txt	A list of new features in prime95.exe.
stress.txt	A discussion of issues relating to stress testing a computer.
undoc.txt	A list of undocumented and unsupported features.
prime.txt	A file containing your preferences.  The menu choices and dialog boxes are used to change your preferences.
worktodo.txt	A list of exponents the program will be factoring and/or Lucas-Lehmer testing.
results.txt	Prime95.exe writes its results to this file.
results.json.txt Prime95.exe writes results to this file in an easy-to-parse JSON format.
results.bench.txt Prime95.exe writes benchmark results to this file.
gwnum.txt	A file containing benchmark data.  Used to tune FFTs for your particular machine. 
prime.log	A text file listing all messages that have been sent to the PrimeNet server.
prime.spl	A binary file of messages that have not yet been sent to the PrimeNet server.
mprime.pid	Linux only.  The PID of the currently running mprime.
cNNNNNNN,cNNNNNNN.buN	Intermediate files produced during certification runs.
pNNNNNNN,pNNNNNNN.buN	Intermediate files produced by prime95.exe to resume computation where it left off.
pNNNNNNN.residues	Large intermediate file produced during PRP test for constructing a PRP proof.
pNNNNNNN.proof		PRP proof file.
eNNNNNNN,eNNNNNNN.buN	Intermediate files produced during ECM factoring.
fNNNNNNN,fNNNNNNN.buN	Intermediate files produced during trial factoring.
mNNNNNNN,mNNNNNNN.buN	Intermediate files produced during P-1 factoring.
nNNNNNNN,nNNNNNNN.buN	Intermediate files produced during P+1 factoring.


WHAT IS THIS PROGRAM?
---------------------

This program is used to find Mersenne Prime numbers.  See
https://primes.utm.edu/mersenne/index.html for a good
description of Mersenne primes.  Mersenne numbers can be proved
composite (not prime) by either finding a factor or by running
a PRP (PRobable Prime) or Lucas-Lehmer primality test.

The preferred primality test is PRP because it allows vastly superior error checking
as well as the ability to generate a separate proof file that proves the test was
run and the results were correct.

WHERE TO GET HELP
-----------------

The best help is available by asking a question at mersenneforum.org.  It can be
a little overwhelming at first.  Especially with acronyms commonly used as a
shorthand (there even used in this file).  So here is a quick list of some of
the more common acronyms.
	PRP - PRobable Prime test (this is how we find new Mersenne primes)
	LL - Lucas-Lehmer test (this is how we used to find new primes)
	DC - Double-Checking (LL tests must be double-checked with a second LL test)
	TF - Trial Factoring (can eliminate some candidates cheaply)
	P-1 - P-1 factoring (also eliminates candidates cheaply)
	P+1 - P+1 factoring (used in an effort to fully factor tiny Mersennes)
	ECM - Elliptic Curve Method factoring (used in an effort to fully factor tiny Mersennes)
	CERT - CERTification (certifies PRP proofs, better and cheaper than a DC) 
	GEC - Gerbicz Error Checking (powerful error cheking and recovery for PRP tests)
	GPU72 - subproject to TF candidates on GPUs (GPUs TF much better than CPUs)

INSTRUCTIONS
------------

Create a directory to hold the executable and associated files.  Make sure you have
write access to this new directory.  In Windows, this may mean choosing a different
directory than the standard Windows unzip utility suggests!  Unzip your download
into this new directory.  

Now run the program.  In Windows, double-click on prime95.  In Linux, FreeBSD,
Mac OS X command line version, cd to the directory and type "./mprime -m".
You may need to install the included libraries.

There are two ways to use this program.  The automatic way uses a central server,
which we call the PrimeNet server, to get work to do and report your results.
You do not need a permanent connection to the Internet.

The second method is the manual method.  It requires a little more work
and monitoring.  This is recommended for computers with no Internet access
or with some kind of firewall problem that prevents the automatic method
from working.

If you are running this program at your place of employment, you must
first GET PERMISSION from your network administrator, boss, or both.
This is especially true if you are installing the software on several machines.
Many companies have policies that prohibit running unauthorized software.
Violating that policy could result in TERMINATION and/or PROSECUTION.


INSTRUCTIONS FOR THE AUTOMATIC METHOD
-------------------------------------

1)  Connect to the Internet.  Create an account at https://mersenne.org
2)  Run prime95.exe on Windows or "mprime -m" on Linux.  You will see 5 dialog boxes:
2a) In the welcome dialog box, choose "Join GIMPS!".
2b) In the second dialog box, enter your user id and optional computer name.
    If you are using several computers, use the same user ID but a unique computer
    name on each machine.  An easy-to-remember user ID will be helpful if you
    plan to visit the PrimeNet server's web page to view reports on your progress.
2c) In the third dialog box, fill in roughly how many hours a day you leave
    your computer running.  Click OK (just close the dialog box on a Mac).
2d) In the fourth dialog box, place constraints on the resources the program
    is allowed to use.
2e) In the fifth dialog box, choose the number of workers you want to
    run.  Choose the type of work you want each worker to perform.
3a) If a proxy server is the causing connection troubles, see the
    later section on "SETTING UP A PROXY SERVER".
3b) If the program will not connect to the server, then
    you will have to use the manual method described below.
4)  Disable screen savers or use the "blank screen" screen saver.  If this
    is not practical, consider raising prime95's priority to 4 or 5.


MANUAL METHOD INSTRUCTIONS
--------------------------

1)  Visit https://mersenne.org/update/ to create a userid for yourself and
    https://mersenne.org/manual_assignment/ to get an exponent or two to
    work on.  Copy these exponents to a file called worktodo.txt.
2)  Run prime95.exe on Windows or "mprime -m" on Linux.  You will see 3 dialog boxes:
2a) In the welcome dialog box, choose "Join GIMPS!".
2b) In the second dialog box, uncheck "Use PrimeNet to get work and report
    results", click OK.
2c) In the third dialog box, place constraints on the resources the program
    is allowed to use.
3)  Disable screen savers or use the "blank screen" screen saver.  If this
    is not practical, consider raising the program's priority to 4 or 5.
4)  When done with your exponents, use the web pages again to send the
    file "results.json.txt" to the PrimeNet server and get more work.


WARNINGS and NOTES
------------------

Running this program may SIGNIFICANTLY INCREASE YOUR ELECTRIC BILL.  The amount
depends on your computer and your local electric rates.

It can take many CPU weeks to test a large Mersenne number.  This program
can be safely interrupted by using the ESC key to write intermediate results
to disk.  This program also saves intermediate results to disk every 30 minutes
in case there is a power failure.

You can compare your computer's speed with other users by checking the
web page https://mersenne.org/report_benchmarks/.  If you are much slower
than comparable machines, utilities such as Task Manager are available
that can find programs that are using CPU cycles.

You can get several reports of your PrimeNet activity at any time
by logging in at https://mersenne.org/.

If you have overclocked your machine, I recommend running the torture
test for a few hours.  The longer you run the torture test the greater the
chance that you will uncover possible problems caused by overheating
or overstressed memory.

Depending on the exponent being tested, the program may decide that it
would be wise to invest some time checking for small factors using trial
factoring or P-1 factoring before running a primality test.


CONTROLLING RESOURCE USAGE
--------------------------

The Options/Resource Limits dialog box contains important settings to control
the program's resource usage.

The PRP primality test generates a proof file that eliminates the need to double-check your results.
Generating these proofs requires lots of temporary disk space.  Proof files must be uploaded to the
server which could be an issue for some users with limited bandwidth.

PRP proofs are a little more efficient when using a larger proof power.  However, this requires more
disk space and internet bandwidth.  The table below shows resource usage for several proof powers
and exponents.

Proof                              Temp disk space / Proof file size
Power         Exponent=100,000,000         Exponent=200,000,000            Exponent=332,000,000
7               1.6GB / 100MB                  3.2GB / 200MB                  5.3GB / 332MB
8               3.2GB / 113MB                  6.4GB / 225MB                  10.6GB / 373MB
9               6.4GB / 125MB                  12.8GB / 250MB                 21.2GB / 415MB
10             12.8GB / 138MB                  25.6GB / 275MB                 42.4GB / 457MB

After the proof is uploaded to the server it is pre-processed creating a smaller file that
must be downloaded by users doing proof certification work.  Proof certification work is
very fast (128, 256, 512, or 1024 times faster than the original PRP test depending on proof
power).  By default, your computer is signed up for occasional proof certification work.

Temporary disk usage
--------------------

This setting limits the amount of temporary disk space used BY EACH WORKER doing PRP work.
See the table above for how that affects proof power.  A proof power of 8 or more is desirable,
but a proof power of 7 is acceptable.  The default limit of 10GB will use a proof power of 9
for exponents you are likely to be assigned for first time tests over the next several years.

Upload bandwidth limit
----------------------

For most home users, download speed is faster than upload speed.  If upload speed is not limited,
it can impact download speeds.  I recommend not using more than 10% of your upload bandwidth.
The default setting is 1.0Mbps.  This uploads a 100MB proof file in about an hour.  If the upload
is interrupted, it will automatically resume from where it left off at a later time.

Upload time period
------------------

You can further reduce impact on network performance by scheduling uploads for off hours when
network traffic is light.

Download limit for certification work
-------------------------------------

This controls the number of MB that can be downloaded to your machine each day for certification work.
It is highly unlikely there will be enough certification work available to satisfy the default setting
of 40MB / day.  If you are on a metered connection or do not wish to devote any CPU time to certification
work then you can set this to zero and you will not be assigned certification work.  You can also use
this setting to control how often you get certification assignments.  For example, with exponents around
100,000,000 requiring a 12MB download, setting the download limit to 4MB would mean getting at most one
100,000,000 certification every 3 days.

 
ADVANCED RESOURCE USAGE
-----------------------

These resource settings shouldn't be changed for most users.

Directory for large temporary files
-----------------------------------

The large temporary files needed for PRP proofs are stored in the same directory as the program.
If another disk drive or a network disk has considerably more free space, you can use this
setting to name a different directory to store the large temporary proof files.

Directory to hold archived proofs
---------------------------------

The server deletes the uploaded proof files once the result has been certified.  If you would
like to archive your proof files for posterity, name the directory to store the proofs.  After
uploading to the server, the proof files will be moved to this directory.

Daytime and nighttime P-1/P+1/ECM stage 2 memory
------------------------------------------------

On occasion, you may be assigned an exponent that needs P-1 factoring prior to running a
primality test.  Or you might choose to do P-1, P+1, or ECM work (possibly because you do
not want to devote disk space for large PRP files).  In these situations, the program can
run stage 2 more effectively if it is given more memory to work with.  However, if you let
the program use too much memory then the performance of ALL programs will suffer due to thrashing.  

That is, most of the time this setting is not used and even with minimal settings the program
will work just fine.  Should you decide to change these settings, how do you choose intelligently?
Below are some steps you might take to figure this out:

1)  Be aggressive for P-1, conservative for P+1 and ECM.  The P-1 stage 2 algorithm LOVES memory.
Double the memory, double the throughput.  For P+1 and ECM it is better to set the memory too low than
too high.  For all work types setting the value too high can cause thrashing which slows down all programs.

2)  Start with how much memory is installed in your machine.  Allow a reasonable amount of memory
for the OS and whatever background tasks you run (say 2GB to 6GB).  This represents the maximum
value you should use.  The program won't let you enter more than 90% of installed memory.

3)  Assuming you run your machine 24 hours a day, what hours of the day do you not use your computer?
Make these your nighttime hours and let the program use a lot of memory during these hours.  But
reduce this value if you also run batch jobs at night.

4)  Factor in the information below about minimum, reasonable, and desirable memory amounts for some
sample exponents.  If you choose a value below the minimum, that is OK.  The program will simply skip
stage 2 of P-1 factoring.

	Exponent	Minimum		Workable	Desirable
	--------	-------		--------	---------
	100000000	 0.2GB		  0.7GB		 85-90% of RAM
	333000000	 0.7GB		  2.1GB		 85-90% of RAM

For example, my machine is a quad-processor with 16GB of memory.  I guess Windows and the programs I
normally use can survive on 4GB of memory.  Thus, I set memory to 12.0GB.  This is my nighttime setting.
During the day, I run more programs, so I set memory to 4.0GB.  I can always stop prime95 if it is
doing stage 2 work and I suspect memory is thrashing.  More casual users might want to set the
daytime memory to 0.1GB so they never have to worry about prime95 impacting system performance.

Max emergency memory
--------------------

If the program cannot write to the large temporary file, it will use emergency memory to hold data in
hopes that it can later be successfully written to the temporary file.  For me, this gives me about a day
to correct a network drive that has gone offline.  If you use a local disk to store your large temporary
proof files then emergency memory might be used if the local disk is full.

CPU Priority
------------

It is strongly recommended that you use the default priority of 1.  This program is unlikely to run any
faster at a higher priority.  Priority 1 is the lowest priority and 10 is the highest.  Screen savers
run at priority 4.  Most applications run at priority 7 or 9.  The only time you should raise the
priority is when another background process, such as a screen saver, is not leaving many spare CPU
cycles for this program.

Certification work limit
------------------------

This setting limits the maximum amount of CPU time that will be spent doing certification work.  It is
highly unlikely there will be enough certification work available to satisfy the default setting of 10%.
If every user does their share of certification work then there will only be enough work for less
than 0.5% of your CPU time.

NOTE: Technically, the default limit is not 10% of your CPU, but rather 10% of the work a typical 2015
quad-core CPU can perform in a day.

Use hyperthreading
------------------

Except for trial factoring, which is best left for GPUs to do, hyperthreading often offers no performance
benefit while using more electricity.  You can try test if hyperthreading speeds up your worker windows by
selecting these options.



SETTING UP A PROXY SERVER
-------------------------

Choose the "Connection..." button in the Test/Primenet dialog box.  Fill in
the proxy information.


PROGRAM OUTPUT
--------------

On screen you will see:

Factoring M400037 to 2^54 is 3.02% complete. Time: 0.121 sec.
	This means prime95/mprime is trying to find a small factor of 2^400037-1.
	It is 3.02% of the way though looking at factors below 2^54.  When
	this completes it may start looking for factors less than 2^55.
Iteration: 9414000 / 96774711 [9.73%].  Per iteration time: 0.109 sec.
	This means prime95/mprime just finished the 9414000th iteration of a
	primality test.  The program must execute 96774711 iterations to
	complete the primality test.  The average iteration took 0.109 seconds.

The results file and screen will include lines that look like:

M2645701 has a factor: 13412891051374103
	This means to 2^2645701-1 is not prime.  It is divisible by 13412891051374103.
M2123027 no factor to 2^57, WV1: 14780E25
	This means 2^2123027-1 has no factors less than 2^57.  The Mersenne
	number may or may not be prime.  A primality test is needed
	to determine the primality of the Mersenne number.  WV1 is
	the program version number.  14780E25 is a checksum to guard
	against transmission errors.
M1992031 is not prime. Res64: 6549369F4962ADE0. WV1: B253EF24,1414032,00000000
	This means 2^1992031-1 is not prime - a primality test says so.
	The last 64 bits of the last number in the primality test sequence
	is 6549369F4962ADE0.  At some future date, another person will verify
	this 64-bit result by rerunning the primality test.  WV1 is the
	program	version number.  B253EF24 is a checksum to guard against
	transmission errors.  1414032 can be ignored it is used as part
	of the double-checking process.  The final 00000000 value is a set
	of 4 counters.  These count the number of errors that occurred during
	the Lucas-Lehmer test.
M11213 is prime! WV1: 579A579A
	This means 2^11213-1 is a Mersenne prime!  WV1 is the program
	version number.  579A579A is a checksum to guard against
	transmission errors.


INSTALLING ON SEVERAL COMPUTERS
-------------------------------

Download and install on each computer following the instructions above.
Use Test/Primenet to give each computer the same user id and different computer name.

Do not copy the installation from one computer to another computer.  There are
settings in prime.txt that must be different on each computer.


TEST MENU
---------

The PrimeNet menu choice identifies your computer to the server.
The "Use PrimeNet..." option can be turned on to switch from the
manual method to the automatic method.

The Worker Windows menu choice is used to choose the type of work
you'd prefer to execute.  The work preference should usually be left
set to "Whatever makes the most sense".  However, if you are running a
slow computer and don't mind waiting several months for a single primality
test to complete OR you are running a faster computer and prefer
faster work types, then choose a different work preference.

The Status menu choice will tell you what exponents you are working on.
It will also estimate how long that will take and your chances of finding
a new Mersenne prime.

The Continue menu choice lets you resume prime95/mprime after you have stopped it.

The Stop menu choice lets you stop the program.  When you continue,
you will pick up right where you left off.  This is the same as hitting
the ESC key.

The Exit menu choice lets you exit the program.


ADVANCED MENU
-------------

You should not need to use the Advanced menu.  This menu choice is
provided only for those who are curious.  Note that many of the menu choices
are grayed while testing is in progress.  Choose Test/Stop to activate
these menu choices.

The Test choice can be used to run a Lucas-Lehmer test on one Mersenne
number.  Enter the Mersenne number's exponent - this must be a prime
number between 5 and 560000000.

The Time choice can be used to see how long each iteration of a Lucas-Lehmer
test will take on your computer and how long it will take to test a
given exponent.  For example, if you want to know how long a Lucas-Lehmer
test will take to test the exponent 876543, choose Advanced/Time and
enter 876543 for 100 iterations.

The ECM choice lets you factor numbers of the form k*b^n+c using the
Elliptic Curve Method of factoring.  ECM requires a memory minimum of 192 times
the FFT size.  Thus, ECM factoring of F20 which uses a 64K FFT will use
a minimum of 192 * 64K or 12MB of memory.  You can also edit the
worktodo.txt file directly.  For example:
	ECM2=k,b,n,c,B1,B2,curves_to_run[,"comma-separated-list-of-known-factors"]

The P-1 choice lets you factor numbers of the form k*b^n+c using
the P-1 method of factoring.  You can also edit the worktodo.txt file
directly.  For example:
	Pminus1=k,b,n,c,B1,B2[,"comma-separated-list-of-known-factors"]

The PRP choice, available from the menus only in the Mac OS X version, lets you do a
probable prime test on numbers of the form k*b^n+c.  On all OSes, you can edit
the worktodo.txt file directly.  For example add:
	PRP=k,b,n,c[,how_far_factored,tests_saved][,prp_base,residue_type][,"comma-separated-list-of-known-factors"]
where the how_far_factored and tests_saved values are used to pick
optimal bounds for P-1 factoring prior to running the PRP test.

Round off checking.  This option will slow the program down a little.
This option displays the smallest and largest "convolution error".  The
convolution error must be less than 0.49 or the results will be incorrect.
There really is no good reason to turn this option on.

The Manual Communication menu choice should only be used if the
automatic detection of an Internet connection is not working for you.
Using this option means you have to remember to communicate with the
server every week or two (by using this same menu choice).

The Unreserve Exponent choice lets you tell the server to unreserve
an exponent you have been assigned.  You might do this if a second computer
you had been running GIMPS on died or if you had been assigned an exponent
of one work type (such as a first-time-test) and now you have switched to
another work type (such as double-checking).  Any work you have
done on the unreserved exponent will be lost.

The Quit GIMPS menu choice is used when you no longer want this computer
to work on the GIMPS project.  You may rejoin at a later date.
If you are a PrimeNet user your unfinished work will be returned to the
server.  If you are a manual user, submit your final results to
the web page https://mersenne.org/manual_result


OPTIONS MENU
------------

The CPU menu choice tells you what CPU the program has detected and
lets you set how many hours a day you expect to run the program.  This setting
is used to give better estimated completion dates in Test / Status.  It may
also affect what work you are assigned in for "What makes the most sense" work
preference.

The Resource limits menu choice lets you set several important resource limitations
for the program.  These were discussed extensively above.

The Preferences menu choice lets you control how often lines are written to the
main window and how often lines are written to the results file.  It also lets
you change how often intermediate files (to guard against power failure and crashes)
are created.  You can control how often the program checks to see if you are connected
to the Internet.  The program polls whenever it has new data to send to or work to get
from the PrimeNet server.  If you are low on disk space, you can select one intermediate 
file instead of more.  However, if you crash in the middle of writing the one intermediate
file, you may have to restart an exponent from scratch.  You can also tell the program to
be quiet, rather than beeping like crazy, if a new Mersenne prime is found.  You can also
make prime95/mprime go idle whenever your laptop is running on battery power.

The Torture Test choice will run a continuous self test.  This is great
for testing machines for hardware problems.  See the file stress.txt
for a more in-depth discussion of stress testing and hardware problems.

The Benchmark choice determines your computer's throughput on several FFT lengths.
Use this to decide the best number of worker windows to run.

The Tray Icon choice will cause prime95 to have a small icon on the taskbar
when it is minimized.  You can activate or hide the program by double-clicking
on the small icon.  If you place the cursor over the small icon, a tooltip will
display the current status.

The No Icon choice means there will be no prime95 icon on the taskbar once you
minimize the program - making it very hard to reactivate!  You can reactivate
the program by trying to execute prime95 a second time.  Alternatively, you can
turn this feature off by editing prime.txt and change the line "HideIcon=1" to
"HideIcon=0", then reboot.


PRIME95 COMMAND LINE ARGUMENTS
------------------------------

-t		Run the torture test.  Same as Options/Torture Test.
-Wdirectory	This tells prime95 to find all its files in a different
		directory than the executable.

MPRIME COMMAND LINE ARGUMENTS
----------------------------

-c		Contact the PrimeNet server then exit.  Useful for
		scheduling server communication as a cron job or
		as part of a script that dials an ISP.
-d		Prints more detailed information to stdout.  Normally
		mprime does not send any output to stdout.
-m		Bring up the menus to set mprime's preferences.
-t		Run the torture test.  Same as Options/Torture Test.
-v		Print the version number of mprime.
-Wdirectory	This tells mprime to find all its files in a different
		directory than the executable.


POSSIBLE HARDWARE FAILURE
-------------------------

If the message "Possible hardware failure, consult the readme file."
appears in the results.txt file, then prime95/mprime's error-checking has
detected a problem.  After waiting 5 minutes, the program will continue
testing from the last save file.

The most common error message is ROUND OFF > 0.40 caused by one of two things:
	1)  For reasons too complicated to go into here, the program's error
	checking is not	perfect.  Some errors can be missed and some correct
	results flagged as an error.  If you get the message "Disregard last
	error..." upon continuing from the last save file, then you may have
	found the rare case where a good result was flagged as an error.
	2)  A true hardware error.

If you do not get the "Disregard last error..." message or this happens
more than once, then your machine is a good candidate for a torture test.
See the stress.txt file for more information.

Could it be a software problem (bug)?  Unlikely.  Try running a torture test
and/or asking for advice at mersenneforum.org.

Running the program on a computer with hardware problems will still produce
correct PRP results.  PRP primality tests have exceptionally strong error 
recovery mechanisms.  Plus, the final result can be proven correct with a
quick certification of the PRP proof file.


FERMAT PRP AND LUCAS-LEHMER DETAILS
-----------------------------------

This program uses a Fermat probable prime test (PRP) to see if 2^p-1 is prime.
The Fermat PRP test is defined as:
	2^p-1 is probably prime if 3^((2^p-1)-1) = 1 mod (2^p - 1)
	This is equivalent to 3^(2^p-1) = 3 mod (2^p - 1)
	This is equivalent to 3^(2^p) = 9 mod (2^p - 1)
This is simply p squarings modulo 2^p-1.

This program can also perform a Lucas-Lehmer primality test to see if 2^p-1 is prime.
The Lucas sequence is defined as:
	L[1] = 4
	L[n+1] = (L[n]^2 - 2) mod (2^p - 1)
2^p-1 is prime if and only if L[p-1] = 0.
This requires p-2 squarings modulo 2^p-1 and p-2 subtractions of 2.

Fermat PRP testing is greatly preferred over Lucas-Lehmer testing because
of the robust error recovery and quick proof file verification.

This program uses a discrete weighted transform (see Mathematics of
Computation, January 1994) to square numbers mod 2^p - 1.


COPYRIGHTS (hwloc library)
--------------------------

Copyright © 2004-2006 The Trustees of Indiana University and Indiana University Research and Technology Corporation.  All rights reserved.
Copyright © 2004-2005 The University of Tennessee and The University of Tennessee Research Foundation.  All rights reserved.
Copyright © 2004-2005 High Performance Computing Center Stuttgart, University of Stuttgart.  All rights reserved.
Copyright © 2004-2005 The Regents of the University of California. All rights reserved.
Copyright © 2009      CNRS
Copyright © 2009-2016 Inria.  All rights reserved.
Copyright © 2009-2015 Université Bordeaux
Copyright © 2009-2015 Cisco Systems, Inc.  All rights reserved.
Copyright © 2009-2012 Oracle and/or its affiliates.  All rights reserved.
Copyright © 2010      IBM
Copyright © 2010      Jirka Hladky
Copyright © 2012      Aleksej Saushev, The NetBSD Foundation
Copyright © 2012      Blue Brain Project, EPFL. All rights reserved.
Copyright © 2013-2014 University of Wisconsin-La Crosse. All rights reserved.
Copyright © 2015      Research Organization for Information Science and Technology (RIST). All rights reserved.
Copyright © 2015-2016 Intel, Inc.  All rights reserved.

THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


COPYRIGHTS (QD - QUAD-DOUBLE/DOUBLE-DOUBLE COMPUTATION PACKAGE)
---------------------------------------------------------------

Copyright (c) 2003, The Regents of the University of California,
through Lawrence Berkeley National Laboratory (subject to receipt of
any required approvals from U.S. Dept. of Energy) 

All rights reserved. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. 


COPYRIGHTS (libcurl)
--------------------

Copyright (c) 1996 - 2016, Daniel Stenberg, daniel@haxx.se, and many contributors, see the THANKS file.
All rights reserved.
Permission to use, copy, modify, and distribute this software for any purpose with or without fee is hereby granted,
provided that the above copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT OF THIRD PARTY RIGHTS. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.


COPYRIGHTS (libgmp)
-------------------

Copyright 1991, 1996, 1999, 2000, 2007 Free Software Foundation, Inc.

This file is part of the GNU MP Library.

The GNU MP Library is free software; you can redistribute it and/or modify
it under the terms of either:

  * the GNU Lesser General Public License as published by the Free
    Software Foundation; either version 3 of the License, or (at your
    option) any later version.

or

  * the GNU General Public License as published by the Free Software
    Foundation; either version 2 of the License, or (at your option) any
    later version.

or both in parallel, as here.

The GNU MP Library is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
for more details.

You should have received copies of the GNU General Public License and the
GNU Lesser General Public License along with the GNU MP Library.  If not,
see https://www.gnu.org/licenses/.


COPYRIGHTS (cJSON)
-------------------

  Copyright (c) 2009-2017 Dave Gamble and cJSON contributors

  Permission is hereby granted, free of charge, to any person obtaining a copy
  of this software and associated documentation files (the "Software"), to deal
  in the Software without restriction, including without limitation the rights
  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
  copies of the Software, and to permit persons to whom the Software is
  furnished to do so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in
  all copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
  THE SOFTWARE.


OUR DISCLAIMER
--------------

THIS PROGRAM AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF
ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO
THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
PARTICULAR PURPOSE.


THANKS
------

Happy hunting and thanks for joining the search,
George Woltman
woltman@alum.mit.edu

